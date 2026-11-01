using Microsoft.JSInterop;

namespace ResilientTranslator.MainProject.Infrastructure.Speech;

public enum SpeechState { Idle, Preparing, Playing }

/// <summary>Owns the latest speech request and a bounded browser recording cache.</summary>
public sealed class SpeechPlayback : IAsyncDisposable
{
    const bool IsLogEnabled = true;
    readonly IJSRuntime javascript;
    readonly IEnglishPhonemizer phonemizer;
    PronunciationEngine Selected;

    public SpeechPlayback(IJSRuntime javascript,IEnglishPhonemizer phonemizer)
    {
        this.javascript=javascript;this.phonemizer=phonemizer;Selected=phonemizer.Engine;
        Preferences.Changed+=OnPreferencesChanged;
    }
    readonly SemaphoreSlim Initialization = new(1, 1);
    IJSObjectReference? Controller, Module;
    CancellationTokenSource? Current;
    string? LastText;
    int ListenCount;
    public object? Owner { get; private set; }
    public SpeechState State { get; private set; }
    public object? FailedOwner { get; private set; }
    public event Action? Changed;

    /// <summary>Stops the active command or starts this editor's current text.</summary>
    public async Task ToggleAsync(object owner, string text)
    {
        if (Owner == owner && State != SpeechState.Idle) { await StopAsync(); return; }
        if (string.IsNullOrWhiteSpace(text)) return;
        
        var engine=phonemizer.Engine;
        var request = new CancellationTokenSource();
        var previous = Current; Current = request; previous?.Cancel();
        Owner = owner; FailedOwner = null; State = SpeechState.Preparing; Changed?.Invoke();
        var outcome = await ExceptionBoundary.RunSafelyAsync(async token =>
        {
            await InitializeAsync(token);
            var controller = Controller ?? throw new InvalidOperationException("Speech controller is unavailable.");
            token.ThrowIfCancellationRequested();
            await controller.InvokeVoidAsync("stop", token);
            bool cached = await controller.InvokeAsync<bool>("has", token, text,engine.ToString());
            Log($"Speech requested; cached={cached}; text={text}", Run: IsLogEnabled);
            if (!cached)
            {
                string[] sentences = await phonemizer.ConvertAsync(text, token);
                await Resilience.ExecuteResilientlyAsync(async ct =>
                {
                    try { await controller.InvokeVoidAsync("prepare", ct, text, sentences,engine.ToString()); return true; }
                    catch (JSException) when (!ct.IsCancellationRequested)
                    {
                        await controller.InvokeVoidAsync("reset", ct);
                        throw;
                    }
                }, error => error is JSException, token, retryCount: 1, attemptTimeout: TimeSpan.FromMinutes(2), totalTimeout: TimeSpan.FromMinutes(4), operationName: "GenerateEnglishSpeech");
            }
            token.ThrowIfCancellationRequested();
            if (Current != request) return;
            if (LastText != text) { LastText = text; ListenCount = 0; }
            double rate = ++ListenCount % 2 == 0 ? 0.9 : 1;
            State = SpeechState.Playing; Changed?.Invoke();
            Log($"Speech playback started; listen={ListenCount}; rate={rate}; cached={cached}", Run: IsLogEnabled);
            await controller.InvokeAsync<bool>("play", token, rate);
        }, request.Token, operationName: "SpeakEnglish");
        if (Current == request)
        {
            if(outcome.Status == OperationStatus.Failed) FailedOwner = owner;
            if (!outcome.Succeeded && Controller is not null)
                await ExceptionBoundary.RunSafelyAsync(async _ => await Controller.InvokeVoidAsync("stop"), operationName: "StopFailedSpeech");
            Current = null; Owner = null; State = SpeechState.Idle; Changed?.Invoke();
        }
        request.Dispose();
    }

    async Task InitializeAsync(CancellationToken token)
    {
        await Initialization.WaitAsync(token);
        try
        {
            if (Controller is not null) return;
            Module ??= await javascript.InvokeAsync<IJSObjectReference>("import", token, "./_content/MainProject/speech/playback.js");
            Controller = await Module.InvokeAsync<IJSObjectReference>("create", token);
        }
        finally { Initialization.Release(); }
    }

    void OnPreferencesChanged()
    {
        if(Selected==phonemizer.Engine)return;
        Selected=phonemizer.Engine;LastText=null;ListenCount=0;
        _=ExceptionBoundary.RunSafelyAsync(async _=>await StopAsync(),operationName:"ChangeSpeechPronunciation");
    }

    /// <summary>Cancels generation at its worker and stops playback without clearing the last recording.</summary>
    public async Task StopAsync()
    {
        var previous = Current; Current = null;
        previous?.Cancel(); Owner = null; State = SpeechState.Idle; Changed?.Invoke();
        if (Controller is not null)
            await ExceptionBoundary.RunSafelyAsync(async _ => await Controller.InvokeVoidAsync("stop"), operationName: "StopEnglishSpeech");
    }

    public async ValueTask DisposeAsync()
    {
        Preferences.Changed-=OnPreferencesChanged;
        await StopAsync();
        if (Controller is not null) { await Controller.InvokeVoidAsync("dispose"); await Controller.DisposeAsync(); }
        if (Module is not null) await Module.DisposeAsync();
        Initialization.Dispose();
    }
}
