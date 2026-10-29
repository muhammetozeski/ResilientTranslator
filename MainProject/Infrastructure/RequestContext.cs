namespace ResilientTranslator.MainProject.Infrastructure;
/// <summary>Owns one user command's cancellation and worker; object identity determines whether it may publish a result.</summary>
public sealed class RequestContext
{
    const bool IsLogEnabled = true;
    static long NextId;
    readonly CancellationTokenSource Cancellation = new();
    TranslationJob? Job;
    int Canceled;
    Task CancellationTask = Task.CompletedTask;
    public long Id { get; } = Interlocked.Increment(ref NextId);
    public CancellationToken Token => Cancellation.Token;
    /// <summary>Requests cancellation without waiting for the old network worker.</summary>
    public void Cancel()
    {
        if (Interlocked.Exchange(ref Canceled, 1) != 0) return;
        CancellationTask = ExceptionBoundary.RunSafelyAsync(async token =>
        {
            await Cancellation.CancelAsync().WaitAsync(token).ConfigureAwait(false);
        }, operationName: $"CancelRequest-{Id}");
        Interlocked.Exchange(ref Job, null)?.Cancel();
        Log($"Request superseded; request={Id}", Run: IsLogEnabled);
    }
    /// <summary>Associates a worker with this request and immediately cancels it if ownership already ended.</summary>
    /// <param name="job">The private worker attached to this request owner.</param>
    internal void Attach(TranslationJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        Interlocked.Exchange(ref Job, job)?.Cancel();
        if (Token.IsCancellationRequested) job.Cancel();
    }
    /// <summary>Releases only the worker belonging to this completed invocation.</summary>
    /// <param name="job">The private worker attached to this request owner.</param>
    internal void Detach(TranslationJob job) => Interlocked.CompareExchange(ref Job, null, job);
}
