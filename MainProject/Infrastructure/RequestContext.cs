namespace ResilientTranslator.MainProject.Infrastructure;
/// <summary>Owns one user command's cancellation and worker; object identity determines whether it may publish a result.</summary>
public sealed class RequestContext
{
    static long NextId;
    readonly CancellationTokenSource Cancellation = new();
    TranslationJob? Job;
    public long Id { get; } = Interlocked.Increment(ref NextId);
    public CancellationToken Token => Cancellation.Token;
    /// <summary>Requests cancellation without waiting for the old network worker.</summary>
    public void Cancel()
    {
        _ = Cancellation.CancelAsync();
        Interlocked.Exchange(ref Job, null)?.Cancel();
    }
    /// <summary>Associates a worker with this request and immediately cancels it if ownership already ended.</summary>
    internal void Attach(TranslationJob job)
    {
        Job = job;
        if (Token.IsCancellationRequested) job.Cancel();
    }
    /// <summary>Releases only the worker belonging to this completed invocation.</summary>
    internal void Detach(TranslationJob job) => Interlocked.CompareExchange(ref Job, null, job);
}
