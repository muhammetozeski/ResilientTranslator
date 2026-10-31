using Lingua;
using ResilientTranslator.MainProject.Infrastructure.Detection;
namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Owns one command's dedicated background thread, cancellable engine sessions and progressive results.</summary>
internal sealed class TranslationJob
{
    const bool IsLogEnabled=true;
    readonly CancellationTokenSource Cancellation=new();
    readonly TaskCompletionSource<TranslationUpdate> Completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly long Revision;
    TranslationSession? ActiveSession;
    int Canceled;
    public Task<TranslationUpdate> Result=>Completion.Task;
    public TranslationJob(long revision,TranslationInput input,Action<string> onDetected,Action<TranslationUpdate> publish,Action<string> status)
    {
        Revision=revision;
        new Thread(()=>Execute(input,onDetected,publish,status)){IsBackground=true,Name=$"Translation-{revision}"}.Start();
    }
    public void Cancel()
    {
        if(Interlocked.Exchange(ref Canceled,1)!=0)return;
        var cancellation=Cancellation.CancelAsync();
        _=ExceptionBoundary.RunSafelyAsync(async _=>{try{await cancellation.ConfigureAwait(false);}finally{await Task.Run(StopClients).ConfigureAwait(false);}},operationName:$"CancelTranslation-{Revision}");
        Log($"Translation cancellation requested; request={Revision}",Run:IsLogEnabled);
    }
    void Execute(TranslationInput input,Action<string> onDetected,Action<TranslationUpdate> publish,Action<string> status)
    {
        try{Completion.TrySetResult(ExecuteAsync(input,onDetected,publish,status).GetAwaiter().GetResult());}
        catch(OperationCanceledException)when(Cancellation.IsCancellationRequested){Completion.TrySetCanceled(Cancellation.Token);}
        catch(Exception error){Completion.TrySetException(error);ExceptionBoundary.LogSafely($"Translation worker failed; request={Revision}; error={error}",IsLogEnabled);}
        finally{StopClients();Log($"Translation worker exited; request={Revision}",Run:IsLogEnabled);}
    }
    async Task<TranslationUpdate> ExecuteAsync(TranslationInput input,Action<string> onDetected,Action<TranslationUpdate> publish,Action<string> status)
    {
        var token=Cancellation.Token;string? detected=null;bool detectionFailed=false;
        if(input.SourceLanguage==TranslationLanguageRules.Automatic)
        {
            try
            {
                detected=await DetectorRegistry.DetectAsync(input.Detector,input.Text,token).WaitAsync(TimeSpan.FromMilliseconds(Settings.DetectorModelLoadTimeout.Value+Settings.DetectorTimeout.Value),token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if(detected is null)throw new InvalidDataException("The selected detector could not identify the source language.");
                onDetected(detected);
                Log($"Language detected; request={Revision}; detector={input.Detector}; result={detected}",Run:IsLogEnabled);
            }
            catch(Exception error)when(!token.IsCancellationRequested)
            {
                Log($"Selected detector failed; falling back to Lingua; request={Revision}; detector={input.Detector}; error={error}",Run:IsLogEnabled);
                try
                {
                    detected=await DetectorRegistry.DetectAsync(DetectorKind.Lingua,input.Text,token).WaitAsync(TimeSpan.FromMilliseconds(Settings.DetectorModelLoadTimeout.Value),token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();if(detected is null)throw new InvalidDataException("Lingua could not identify the source language.");onDetected(detected);
                }
                catch(Exception fallbackError)when(!token.IsCancellationRequested)
                {
                    detectionFailed=true;
                    Log($"All local detection failed; preserving provider auto detection; request={Revision}; error={new AggregateException(error,fallbackError)}",Run:IsLogEnabled);
                }
            }
        }
        var direction=TranslationLanguageRules.ResolveRequest(input.SourceLanguage,input.TargetLanguage,detected,input.Routing??AutoRoutingOptions.Default);
        if(detectionFailed)direction=direction with{Source=TranslationLanguageRules.Automatic};
        using var trace=EngineTrace.Enter(Revision,0,TranslationMode.Concurrent);
        return await TranslationCoordinator.ExecuteAsync(Revision,input,direction,async onlineToken=>
        {
            return await TranslationRecovery.ExecuteAsync(input.Text,direction.Source,direction.Target,async(attempt,innerToken)=>
            {
                using var session=new TranslationSession(Revision,attempt.Number);
                Interlocked.Exchange(ref ActiveSession,session)?.Dispose();
                try{return await session.ExecuteAsync(attempt,innerToken).WaitAsync(innerToken).ConfigureAwait(false);}
                finally{Interlocked.CompareExchange(ref ActiveSession,null,session);}
            },onlineToken,bypassCache:input.ForceNetwork).ConfigureAwait(false);
        },publish,status,token).ConfigureAwait(false);
    }
    void StopClients()=>Interlocked.Exchange(ref ActiveSession,null)?.Dispose();
}
