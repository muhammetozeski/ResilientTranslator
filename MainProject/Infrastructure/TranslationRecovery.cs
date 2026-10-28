using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Retries read-only translation calls, switches execution strategy and splits oversized text without publishing partial results.</summary>
public static class TranslationRecovery
{
    const bool IsLogEnabled = true;
    const int ChunkByteLimit = 1000;
    const int MaximumCacheEntries = 128;
    const int MaximumCachedCharacters = 8192;
    static readonly object CacheGate = new();
    static readonly Dictionary<CacheKey,ConfirmedTranslation> Cache = new();
    static readonly Queue<CacheKey> CacheOrder = new();

    /// <summary>Returns only confirmed complete output; final failure retains every exhausted strategy's errors.</summary>
    /// <param name="text">The immutable source text.</param>
    /// <param name="source">The resolved source language.</param>
    /// <param name="target">The resolved destination language.</param>
    /// <param name="translate">Creates and retires an independent engine session for each call.</param>
    /// <param name="token">Cancels attempts, retry delays and remaining chunks.</param>
    public static async Task<ConfirmedTranslation> ExecuteAsync(string text,string source,string target,
        Func<TranslationAttempt,CancellationToken,Task<ConfirmedTranslation>> translate,CancellationToken token=default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(translate);
        token.ThrowIfCancellationRequested();
        var key=new CacheKey(text,source,target);
        lock(CacheGate)
            if(Cache.TryGetValue(key,out var confirmed))
            {
                ExceptionBoundary.LogSafely($"Translation confirmed-cache hit; request={EngineTrace.RequestId}; source={source}; target={target}; length={text.Length}",IsLogEnabled);
                return confirmed;
            }
        ConfirmedTranslation result;
        var segments=Encoding.UTF8.GetByteCount(text)>ChunkByteLimit ? Split(text).ToArray() : null;
        if(segments is not null && (segments.Length>1 || segments[0].Text!=text))
        {
            var output=new StringBuilder();
            var services=new HashSet<string>();
            string function="";
            int part=0;
            foreach(var segment in segments)
            {
                token.ThrowIfCancellationRequested();
                if(segment.Text.Length>0)
                {
                    ExceptionBoundary.LogSafely($"Translation chunk started; request={EngineTrace.RequestId}; part={++part}; length={segment.Text.Length}",IsLogEnabled);
                    var translated=await ExecuteAsync(segment.Text,source,target,translate,token).WaitAsync(token).ConfigureAwait(false);
                    output.Append(translated.Text); services.Add(translated.Service); function=translated.Function;
                }
                output.Append(segment.Separator);
            }
            result=new(output.ToString(),function,string.Join(" / ",services));
        }
        else
        {
            var errors=new ConcurrentQueue<Exception>();
            int attempt=0;
            async Task<ConfirmedTranslation> Invoke(TranslationMode mode,CancellationToken inner)
            {
                inner.ThrowIfCancellationRequested();
                int number=Interlocked.Increment(ref attempt);
                ExceptionBoundary.LogSafely($"Translation attempt started; request={EngineTrace.RequestId}; attempt={number}; mode={mode}; source={source}; target={target}; length={text.Length}",IsLogEnabled);
                try
                {
                    var value=await translate(new(text,source,target,mode,number),inner).WaitAsync(inner).ConfigureAwait(false);
                    inner.ThrowIfCancellationRequested();
                    if(string.IsNullOrWhiteSpace(value.Text)) throw new InvalidDataException("The translation service returned no usable text.");
                    ExceptionBoundary.LogSafely($"Translation attempt succeeded; request={EngineTrace.RequestId}; attempt={number}; mode={mode}; service={value.Service}; text={value.Text}",IsLogEnabled);
                    return value;
                }
                catch(Exception error) when(!token.IsCancellationRequested)
                {
                    errors.Enqueue(error);
                    ExceptionBoundary.LogSafely($"Translation attempt failed; request={EngineTrace.RequestId}; attempt={number}; mode={mode}; error={error}",IsLogEnabled);
                    throw;
                }
            }
            try
            {
                result=await Resilience.ExecuteResilientlyAsync(ct => Invoke(TranslationMode.Concurrent,ct),CanRetry,token,
                    retryCount:2,attemptTimeout:TimeSpan.FromSeconds(12),totalTimeout:TimeSpan.FromSeconds(37),retryDelay:TimeSpan.FromMilliseconds(200),operationName:$"ConcurrentTranslationRecovery-{EngineTrace.RequestId}").WaitAsync(token).ConfigureAwait(false);
            }
            catch(Exception concurrentError) when(!token.IsCancellationRequested)
            {
                if(!CanRetry(concurrentError)) throw;
                errors.Enqueue(concurrentError);
                ExceptionBoundary.LogSafely($"Concurrent recovery exhausted; request={EngineTrace.RequestId}; switching to sequential services; error={concurrentError}",IsLogEnabled);
                try
                {
                    result=await Resilience.ExecuteResilientlyAsync(ct => Invoke(TranslationMode.Sequential,ct),CanRetry,token,
                        retryCount:1,attemptTimeout:TimeSpan.FromSeconds(12),totalTimeout:TimeSpan.FromSeconds(25),retryDelay:TimeSpan.FromMilliseconds(300),operationName:$"SequentialTranslationRecovery-{EngineTrace.RequestId}").WaitAsync(token).ConfigureAwait(false);
                }
                catch(Exception sequentialError) when(!token.IsCancellationRequested)
                {
                    errors.Enqueue(sequentialError);
                    throw new AggregateException("Every translation recovery strategy failed.",errors);
                }
            }
        }
        token.ThrowIfCancellationRequested();
        if(text.Length<=MaximumCachedCharacters && result.Text.Length<=MaximumCachedCharacters)
            lock(CacheGate)
            {
                if(!Cache.ContainsKey(key))
                {
                    if(Cache.Count>=MaximumCacheEntries && CacheOrder.TryDequeue(out var oldest)) Cache.Remove(oldest);
                    CacheOrder.Enqueue(key);
                }
                Cache[key]=result;
            }
        return result;
    }

    /// <summary>Retries only service, transport, timeout and unusable-response failures; deployment and argument faults remain visible.</summary>
    static bool CanRetry(Exception error) => error switch
    {
        FileNotFoundException or DirectoryNotFoundException or FileLoadException or BadImageFormatException => false,
        TranslationServicesException => true,
        HttpRequestException or IOException or InvalidDataException or TimeoutException or Polly.Timeout.TimeoutRejectedException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(CanRetry),
        _ => false
    };

    /// <summary>Splits on Unicode text-element boundaries and carries original whitespace separately.</summary>
    static IEnumerable<Segment> Split(string text)
    {
        int start=0;
        while(start<text.Length)
        {
            int end=start,bytes=0,boundary=-1;
            while(end<text.Length)
            {
                int length=StringInfo.GetNextTextElementLength(text.AsSpan(end));
                int next=Encoding.UTF8.GetByteCount(text.AsSpan(end,length));
                if(bytes+next>ChunkByteLimit && end>start) break;
                if(char.IsWhiteSpace(text,end)) boundary=end;
                bytes+=next; end+=length;
                if(bytes>=ChunkByteLimit) break;
            }
            if(end<text.Length && boundary>start) end=boundary;
            int contentEnd=end;
            while(contentEnd>start && char.IsWhiteSpace(text,contentEnd-1)) --contentEnd;
            int nextStart=end;
            while(nextStart<text.Length && char.IsWhiteSpace(text,nextStart)) ++nextStart;
            yield return new(text[start..contentEnd],text[contentEnd..nextStart]);
            start=nextStart;
        }
    }
    sealed record CacheKey(string Text,string Source,string Target);
    sealed record Segment(string Text,string Separator);
}

/// <summary>Marks an exhausted engine service pass as recoverable without confusing it with caller cancellation.</summary>
public sealed class TranslationServicesException(Exception error) : Exception("The engine service pass failed.",error);




