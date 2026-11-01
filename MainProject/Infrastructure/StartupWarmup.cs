using Microsoft.JSInterop;
using ResilientTranslator.MainProject.Infrastructure.Detection;
using ResilientTranslator.MainProject.Infrastructure.Speech;
using ResilientTranslator.MainProject.Services;

namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Owns startup preparation of retained engines; user work always supersedes warming.</summary>
public sealed class StartupWarmup(IJSRuntime javascript,SpeechPlayback speech,EnglishPronunciation pronunciation)
{
    const bool IsLogEnabled=true;
    public async Task RunAsync(CancellationToken token)
    {
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(token);
        void Cancel()=>stop.Cancel();
        TranslationWorkspace.RequestSuperseded+=Cancel;
        try
        {
            await TranslationRuntime.InitializeAsync(javascript);
            async Task Step(string name,Func<CancellationToken,Task> action)
            {
                stop.Token.ThrowIfCancellationRequested();
                await ExceptionBoundary.RunSafelyAsync(action,stop.Token,operationName:$"Warm-{name}");
            }
            await Step("LocalTranslation",ct=>TranslationRuntime.WarmAsync(ct));
            await Step("Speech",ct=>speech.WarmAsync(ct));
            foreach(var engine in Enum.GetValues<PronunciationEngine>())
                await Step(engine.ToString(),async ct=>await pronunciation.WarmAsync(engine,ct));
            foreach(var detector in DetectorRegistry.Available)
                await Step(detector.Name,async ct=>await DetectorRegistry.DetectAsync(detector.Kind,"This is an English warmup sentence.",ct));
            Log("Retained engine warmup completed",Run:IsLogEnabled);
        }
        finally {TranslationWorkspace.RequestSuperseded-=Cancel;}
    }
}
