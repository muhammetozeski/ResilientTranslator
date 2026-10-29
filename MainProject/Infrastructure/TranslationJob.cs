using Lingua;
using ResilientTranslator.MainProject.Services;
using System.Reflection;
using System.Runtime.Loader;
namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Executes one unchanged translation engine on its own background thread and owns all of its network clients.</summary>
internal sealed class TranslationJob
{
    const bool IsLogEnabled = true;
    static readonly SemaphoreSlim DetectionGate = new(1,1);
    readonly CancellationTokenSource Cancellation = new();
    readonly TaskCompletionSource<(string? Text, Exception? Error, string Function, string Service)> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TranslationSession? ActiveSession;
    readonly long Revision;
    int Canceled;
    public Task<(string? Text, Exception? Error, string Function, string Service)> Result => Completion.Task;

    /// <summary>Starts an independent worker without executing provider initialization or network calls on the UI thread.</summary>
    /// <param name="revision">The editor request identity used in diagnostic records.</param>
    /// <param name="text">The immutable text snapshot for this job.</param>
    /// <param name="source">The source language code.</param>
    /// <param name="target">The target language code.</param>
    public TranslationJob(long revision, TranslationInput input, Task<LanguageDetector> detector, Action<Language> onDetected)
    {
        Revision = revision;
        var worker = new Thread(() => Execute(input,detector,onDetected)) { IsBackground = true, Name = $"Translation-{revision}" };
        worker.Start();
    }

    /// <summary>Marks this job obsolete immediately and schedules network cancellation without waiting on the caller.</summary>
    public void Cancel()
    {
        if (Interlocked.Exchange(ref Canceled, 1) != 0) return;
        ExceptionBoundary.LogSafely($"Translation cancellation requested; revision={Revision}", IsLogEnabled);
        new Thread(() =>
        {
            try { Cancellation.Cancel(); }
            catch (Exception error) { ExceptionBoundary.LogSafely($"Translation cancellation callback failed; revision={Revision}; error={error}", IsLogEnabled); }
            finally { StopClients(); }
        }) { IsBackground = true, Name = $"CancelTranslation-{Revision}" }.Start();
    }

    /// <summary>Runs and contains one isolated engine invocation; no abandoned engine can update application state.</summary>
    void Execute(TranslationInput input, Task<LanguageDetector> detectorTask, Action<Language> onDetected)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var token = Cancellation.Token;
            string text=input.Text,source=input.SourceLanguage,target=input.TargetLanguage;
            if(source==TranslationWorkspace.Automatic)
            {
                try
                {
                    var detector=detectorTask.WaitAsync(TimeSpan.FromMilliseconds(Settings.DetectorLoadBudget.Value),token).GetAwaiter().GetResult();
                    if(!DetectionGate.WaitAsync(TimeSpan.FromMilliseconds(Settings.DetectorGateBudget.Value),token).WaitAsync(token).GetAwaiter().GetResult())
                        throw new TimeoutException("The shared language detector is busy; use provider detection for this request.");
                    try
                    {
                        var detected=detector.DetectLanguageOf(text);
                        token.ThrowIfCancellationRequested();
                        source=detected==Language.Unknown ? TranslationWorkspace.FallbackLanguage : LanguageInfo.IsoCode6391(detected).ToString().ToLowerInvariant();
                        onDetected(detected);
                        ExceptionBoundary.LogSafely($"Language detected; request={Revision}; language={source}; detected={detected}",IsLogEnabled);
                    }
                    finally { DetectionGate.Release(); }
                }
                catch(Exception detectionError) when(!token.IsCancellationRequested)
                {
                    source=TranslationWorkspace.Automatic;
                    ExceptionBoundary.LogSafely($"Offline detection failed; using the original provider's automatic source detection; request={Revision}; error={detectionError}",IsLogEnabled);
                }
            }
            if(target==TranslationWorkspace.Automatic) target=TranslationWorkspace.FallbackLanguage;
            token.ThrowIfCancellationRequested();
            ExceptionBoundary.LogSafely($"Translation worker started; revision={Revision}; thread={Environment.CurrentManagedThreadId}; source={source}; target={target}; text={text}", IsLogEnabled);
            using var trace=EngineTrace.Enter(Revision,0,TranslationMode.Concurrent);
            var confirmed=TranslationRecovery.ExecuteAsync(text,source,target,async (attempt,innerToken) =>
            {
                using var session=new TranslationSession(Revision,attempt.Number);
                Interlocked.Exchange(ref ActiveSession,session)?.Dispose();
                try
                {
                    innerToken.ThrowIfCancellationRequested();
                    return await session.ExecuteAsync(attempt,innerToken).WaitAsync(innerToken).ConfigureAwait(false);
                }
                finally { Interlocked.CompareExchange(ref ActiveSession,null,session); }
            },token,bypassCache:input.ForceNetwork).WaitAsync(token).GetAwaiter().GetResult();
            token.ThrowIfCancellationRequested();
            Completion.TrySetResult((confirmed.Text,null,confirmed.Function,confirmed.Service));
        }
        catch (OperationCanceledException) when (Cancellation.IsCancellationRequested)
        {
            Completion.TrySetCanceled(Cancellation.Token);
            ExceptionBoundary.LogSafely($"Translation worker canceled; revision={Revision}", IsLogEnabled);
        }
        catch (Exception error)
        {
            Completion.TrySetException(error);
            ExceptionBoundary.LogSafely($"Translation worker failed; revision={Revision}; error={error}", IsLogEnabled);
        }
        finally
        {
            StopClients();

            ExceptionBoundary.LogSafely($"Translation worker exited; revision={Revision}; thread={Environment.CurrentManagedThreadId}; elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F1}", IsLogEnabled);
        }
    }

    /// <summary>Stops only this worker's current attempt; each retry owns a different session.</summary>
    void StopClients() => Interlocked.Exchange(ref ActiveSession,null)?.Dispose();
}



