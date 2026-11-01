using Microsoft.JSInterop;
using MisakiSharp;
using System.Text.RegularExpressions;

namespace ResilientTranslator.MainProject.Infrastructure.Speech;

public enum PronunciationEngine { Misaki, Fyls }

/// <summary>Runs the selected original pronunciation pipeline without detecting language.</summary>
public sealed class EnglishPronunciation(IJSRuntime javascript) : IEnglishPhonemizer, IAsyncDisposable
{
    const bool IsLogEnabled=true;
    readonly SemaphoreSlim Gate=new(1,1);
    IJSObjectReference? Module, Bridge;
    IJSObjectReference? Session;
    EnglishG2P? Misaki;
    CancellationToken CurrentToken;
    public PronunciationEngine Engine=>Settings.SpeechPronunciation.Value;

    /// <inheritdoc />
    public Task<string[]> ConvertAsync(string text,CancellationToken token)=>ConvertWithAsync(Engine,text,token);

    /// <summary>Uses an immutable engine selection, including for silent startup preparation.</summary>
    public async Task<string[]> ConvertWithAsync(PronunciationEngine engine,string text,CancellationToken token,bool warmNeural=false)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Module??=await javascript.InvokeAsync<IJSObjectReference>("import",token,"./_content/MainProject/speech/phonemizers/bridge.js");
            Bridge??=await Module.InvokeAsync<IJSObjectReference>("create",token);
            var session=await Bridge.InvokeAsync<IJSObjectReference>("start",token,engine.ToString());Session=session;
            using var cancellation=token.Register(()=>_=ExceptionBoundary.RunSafelyAsync(async _=>await session.InvokeVoidAsync("cancel"),operationName:"CancelPronunciationWorker"));
            CurrentToken=token;
            Log($"Pronunciation started; engine={engine}; text={text}",Run:IsLogEnabled);
            // Misaki's synchronous callback runs only on this background worker, never the renderer.
            return await Task.Run(async ()=>
            {
                var result=new List<string>();
                foreach(string sentence in Regex.Split(text.Trim(),@"(?<=[.!?…])\s+|\n+"))
                {
                    token.ThrowIfCancellationRequested();
                    if(string.IsNullOrWhiteSpace(sentence))continue;
                    string phones;
                    if(engine==PronunciationEngine.Misaki)
                    {
                        Misaki??=new EnglishG2P(espeakFallback:word=>NeuralAsync(PronunciationEngine.Misaki,word,CurrentToken).GetAwaiter().GetResult());
                        phones=Misaki.Phonemize(sentence).Phonemes;
                    }
                    else phones=await NeuralAsync(engine,sentence,token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if(string.IsNullOrWhiteSpace(phones))throw new InvalidDataException($"{engine} produced no pronunciation.");
                    result.Add(phones);
                }
                if(warmNeural && engine==PronunciationEngine.Misaki)await NeuralAsync(engine,"hello",token).ConfigureAwait(false);
                Log($"Pronunciation finished; engine={engine}; sentences={result.Count}",Run:IsLogEnabled);
                return result.ToArray();
            },token).ConfigureAwait(false);
        }
        finally
        {
            CurrentToken=default;
            if(Session is {} session)
            {
                Session=null;
                await ExceptionBoundary.RunSafelyAsync(async _=>{await session.InvokeVoidAsync("finish");await session.DisposeAsync();},operationName:"ReleasePronunciationCommand");
            }
            Gate.Release();
        }
    }
    public Task<string[]> WarmAsync(PronunciationEngine engine,CancellationToken token)=>ConvertWithAsync(engine,"Please present the record.",token,warmNeural:true);
    async Task<string> NeuralAsync(PronunciationEngine engine,string text,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return await Session!.InvokeAsync<string>("convert",token,text);
    }
    public async ValueTask DisposeAsync()
    {
        if(Bridge is not null){await Bridge.InvokeVoidAsync("dispose");await Bridge.DisposeAsync();}
        if(Module is not null)await Module.DisposeAsync();
        Gate.Dispose();
    }
}
