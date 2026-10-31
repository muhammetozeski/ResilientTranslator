using System.Collections.Concurrent;
using System.Diagnostics;
using ResilientTranslator.MainProject.Infrastructure.Localization;
using ResilientTranslator.MainProject.Infrastructure.Offline;
namespace ResilientTranslator.MainProject.Infrastructure;

public enum TranslationQuality { Bergamot=1, Toz=2, Online=3 }
public sealed record TranslationUpdate(string Text,string Service,TranslationQuality Quality,double ElapsedMilliseconds);

/// <summary>Runs isolated providers and publishes only monotonic quality improvements for one request.</summary>
public static class TranslationCoordinator
{
    const bool IsLogEnabled=true;

    internal static async Task<TranslationUpdate> ExecuteAsync(long request,TranslationInput input,LanguageDirection direction,
        Func<CancellationToken,Task<ConfirmedTranslation>> online,Action<TranslationUpdate> publish,Action<string> status,CancellationToken token)
    {

        // Intentional offline mode never evaluates connectivity or creates an online task.
        bool useOnline=Settings.OnlineEnabled.Value;

        var clock=Stopwatch.StartNew();var errors=new ConcurrentQueue<Exception>();var gate=new object();
        TranslationUpdate? best=null;
        using var bergamotStop=CancellationTokenSource.CreateLinkedTokenSource(token);
        using var tozStop=CancellationTokenSource.CreateLinkedTokenSource(token);
        void Accept(string text,string service,TranslationQuality quality)
        {
            token.ThrowIfCancellationRequested();
            if(string.IsNullOrWhiteSpace(text))throw new InvalidDataException("An engine returned empty translation text.");
            lock(gate)
            {
                if(best is not null && quality<=best.Quality)return;
                best=new(text,service,quality,clock.Elapsed.TotalMilliseconds);
                publish(best);
                Log($"Translation quality advanced; request={request}; quality={quality}; service={service}; elapsedMs={clock.Elapsed.TotalMilliseconds:F1}; text={text}",Run:IsLogEnabled);
            }
            if(quality==TranslationQuality.Online){bergamotStop.Cancel();tozStop.Cancel();}
            else if(quality==TranslationQuality.Toz)bergamotStop.Cancel();
        }
        async Task Local(OfflineEngine engine,TranslationQuality quality,CancellationToken localToken)
        {
            try
            {
                localToken.ThrowIfCancellationRequested();
                status(string.Format(FeatureText.Get("EngineStarted"),engine));
                var value=await Resilience.ExecuteResilientlyAsync(ct=>OfflineTranslationBridge.TranslateAsync(engine,input.Text,direction.Source,direction.Target,ct),
                    error=>error is not ArgumentException && error is not OperationCanceledException,localToken,
                    retryCount:Settings.LocalRetries.Value,attemptTimeout:TimeSpan.FromMilliseconds(Settings.LocalTranslationTimeout.Value),
                    totalTimeout:TimeSpan.FromMilliseconds(Settings.LocalTranslationTimeout.Value*(Settings.LocalRetries.Value+1L)+Settings.ModelLoadTimeout.Value),
                    retryDelay:TimeSpan.FromMilliseconds(Settings.LocalRetryDelay.Value),operationName:$"Local-{engine}-{request}").ConfigureAwait(false);
                localToken.ThrowIfCancellationRequested();Accept(value,engine.ToString(),quality);
            }
            catch(OperationCanceledException) when(localToken.IsCancellationRequested){ }
            catch(Exception) when(localToken.IsCancellationRequested){ }
            catch(Exception error)
            {
                errors.Enqueue(error);Log($"Offline engine failed; request={request}; engine={engine}; error={error}",Run:IsLogEnabled);
                status(string.Format(FeatureText.Get("EngineUnavailable"),engine));
            }
        }
        async Task Online()
        {
            for(long round=0;;round++)
            {
                token.ThrowIfCancellationRequested();
                using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);
                budget.CancelAfter(TimeSpan.FromSeconds(Settings.OnlineTotalBudget.Value));
                try
                {
                    status(FeatureText.Get(round==0?"OnlineStarted":"OnlineRetrying"));
                    var result=await online(budget.Token).WaitAsync(budget.Token).ConfigureAwait(false);
                    Accept(result.Text,result.Service,TranslationQuality.Online);return;
                }
                catch(Exception error) when(!token.IsCancellationRequested)
                {
                    errors.Enqueue(error);while(errors.Count>32)errors.TryDequeue(out _);
                    Log($"Online recovery round failed; request={request}; round={round+1}; error={error}",Run:IsLogEnabled);
                    status(FeatureText.Get("OfflineFallback"));
                    await Task.Delay(Settings.OnlineRetryPause.Value,token).ConfigureAwait(false);
                }
            }
        }
        Task Start(Func<Task> work)=>Task.Factory.StartNew(work,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default).Unwrap();
        var tasks=new List<Task>();
        bool useToz=Settings.UseToz.Value && (direction.Source=="tr" || direction.Target=="tr");
        if(Settings.UseBergamot.Value && direction.Source!="auto")tasks.Add(Start(()=>Local(OfflineEngine.Bergamot,TranslationQuality.Bergamot,bergamotStop.Token)));
        if(useToz && direction.Source!="auto")tasks.Add(Start(()=>Local(OfflineEngine.Toz,TranslationQuality.Toz,tozStop.Token)));
        if(useOnline)tasks.Add(Start(Online));
        try
        {
            await Task.WhenAll(tasks).WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return best??throw new AggregateException("No translation engine produced a usable result.",errors);
        }
        finally
        {
            bergamotStop.Cancel();tozStop.Cancel();
            foreach(var task in tasks)_=task.ContinueWith(t=>ExceptionBoundary.LogSafely($"Late engine task failure; request={request}; error={t.Exception}"),TaskContinuationOptions.OnlyOnFaulted);
            Log($"Translation race finished; request={request}; best={best?.Quality}; elapsedMs={clock.Elapsed.TotalMilliseconds:F1}; errors={errors.Count}",Run:IsLogEnabled);
        }
    }
}
