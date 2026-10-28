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
    HttpClient[] Clients = [];
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
        EngineContext? context = null;
        try
        {
            var token = Cancellation.Token;
            string text=input.Text,source=input.SourceLanguage,target=input.TargetLanguage;
            if(source==TranslationWorkspace.Automatic)
            {
                var detector=detectorTask.WaitAsync(token).GetAwaiter().GetResult();
                DetectionGate.WaitAsync(token).WaitAsync(token).GetAwaiter().GetResult();
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
            if(target==TranslationWorkspace.Automatic) target=TranslationWorkspace.FallbackLanguage;
            token.ThrowIfCancellationRequested();
            ExceptionBoundary.LogSafely($"Translation worker started; revision={Revision}; thread={Environment.CurrentManagedThreadId}; source={source}; target={target}; text={text}", IsLogEnabled);
            string directory = Path.GetDirectoryName(typeof(TranslationJob).Assembly.Location)!;
            context = new EngineContext(directory);
            Type engine = context.LoadFromAssemblyPath(Path.Combine(directory, "TranslationEngine.dll")).GetType("ResilientTranslator", throwOnError: true)!;
            Clients = FindClients(engine);
            token.ThrowIfCancellationRequested();
            var translate = engine.GetMethod("TranslateAsync", BindingFlags.Public | BindingFlags.Static)!
                .CreateDelegate<Func<string?, string, string, bool, Task<(string?, Exception?, string, string)>>>();
            Task<(string?, Exception?, string, string)> operation = translate(text, source, target, true);
            try
            {
                var result = operation.WaitAsync(token).GetAwaiter().GetResult();
                token.ThrowIfCancellationRequested();
                Completion.TrySetResult(result);
            }
            finally
            {
                _ = operation.ContinueWith(completed =>
                {
                    if (completed.Exception is not null) ExceptionBoundary.LogSafely($"Abandoned engine failure; revision={Revision}; error={completed.Exception}", IsLogEnabled);
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
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
            context?.Unload();
            ExceptionBoundary.LogSafely($"Translation worker exited; revision={Revision}; thread={Environment.CurrentManagedThreadId}; elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F1}", IsLogEnabled);
        }
    }

    /// <summary>Collects the engine's direct client and each library translator's client without changing their fields or source.</summary>
    static HttpClient[] FindClients(Type engine)
    {
        var clients = new HashSet<HttpClient>();
        foreach (var field in engine.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            object? instance = field.GetValue(null);
            if (instance is HttpClient client) clients.Add(client);
            else if (instance?.GetType().Namespace?.StartsWith("GTranslate", StringComparison.Ordinal) == true)
                foreach (var member in instance.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                    if (member.GetValue(instance) is HttpClient providerClient) clients.Add(providerClient);
        }
        return [.. clients];
    }

    /// <summary>Cancels and disposes only this job's clients so old providers cannot send later requests.</summary>
    void StopClients()
    {
        foreach (HttpClient client in Interlocked.Exchange(ref Clients, []))
        {
            try { client.CancelPendingRequests(); client.Dispose(); }
            catch (Exception error) { ExceptionBoundary.LogSafely($"Translation client shutdown failed; revision={Revision}; error={error}", IsLogEnabled); }
        }
    }

    /// <summary>Keeps each invocation's static provider state separate from the next user request.</summary>
    sealed class EngineContext(string directory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name is "TranslationEngine" or "GTranslate" or "Polly" or "Polly.Core")
                return LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll"));
            return null;
        }
    }
}





