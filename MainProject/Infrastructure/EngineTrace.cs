using System.Diagnostics.Tracing;
using System.Text;
namespace ResilientTranslator.MainProject.Infrastructure;
/// <summary>Routes the unchanged engine's console output and HTTP lifecycle events into the portable logger.</summary>
public sealed class EngineTrace : EventListener
{
    const bool IsLogEnabled = true;
    static EngineTrace? Listener;
    static int Initialized;
    static readonly AsyncLocal<TranslationTrace?> CurrentTrace = new();
    internal static long? RequestId => CurrentTrace.Value?.Request;
    internal static string CurrentDescription => CurrentTrace.Value is { } trace ? $"request={trace.Request}; attempt={trace.Attempt}; mode={trace.Mode}" : "request=none";
    /// <summary>Flows diagnostic correlation through the original engine's asynchronous HTTP calls.</summary>
    /// <param name="request">The diagnostic request label; object identity controls publication instead.</param>
    /// <param name="attempt">The diagnostic attempt number within its owning translation request.</param>
    /// <param name="mode">Whether the engine providers execute concurrently or sequentially.</param>
    /// <returns>A scope that restores the prior asynchronous diagnostic correlation.</returns>
    internal static IDisposable Enter(long request,int attempt,TranslationMode mode)
    {
        var previous=CurrentTrace.Value;
        CurrentTrace.Value=new(request,attempt,mode);
        return new TraceScope(previous);
    }
    /// <summary>Carries request, attempt and execution-mode labels through asynchronous diagnostics.</summary>
    /// <param name="Request">The diagnostic request label; object identity controls publication instead.</param>
    /// <param name="Attempt">The diagnostic attempt number within its owning translation request.</param>
    /// <param name="Mode">Whether the engine providers execute concurrently or sequentially.</param>
    sealed record TranslationTrace(long Request,int Attempt,TranslationMode Mode);
    /// <summary>Restores the preceding diagnostic correlation when one translation scope ends.</summary>
    /// <param name="previous">The diagnostic correlation that must be restored when the scope ends.</param>
    sealed class TraceScope(TranslationTrace? previous) : IDisposable
    {
        bool Disposed;
        /// <summary>Releases this instance&apos;s subscriptions, callbacks or private resource ownership.</summary>
        public void Dispose() { if(!Disposed) { Disposed=true; CurrentTrace.Value=previous; } }
    }
    /// <summary>Installs process diagnostics before translation providers are initialized.</summary>
    public static void Initialize()
    {
        if (Interlocked.Exchange(ref Initialized,1) != 0) return;
        Listener = new EngineTrace();
        Console.SetOut(TextWriter.Synchronized(new EngineOutput()));
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ExceptionBoundary.LogSafely($"Unhandled exception; terminating={args.IsTerminating}; error={args.ExceptionObject}", IsLogEnabled, waitForLogging: true);
        TaskScheduler.UnobservedTaskException += (_, args) => { ExceptionBoundary.LogSafely($"Unobserved task exception; error={args.Exception}", IsLogEnabled); args.SetObserved(); };
        ExceptionBoundary.LogSafely("Engine console and HTTP diagnostics attached", IsLogEnabled);
    }
    /// <summary>Enables the runtime's HTTP request, response and failure events.</summary>
    /// <param name="source">The runtime diagnostic provider being registered.</param>
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "System.Net.Http") EnableEvents(source, EventLevel.Informational);
    }
    /// <summary>Records network operation names and every payload field supplied by the runtime.</summary>
    /// <param name="data">The callback payload supplied by its Windows/runtime event or application publisher.</param>
    protected override void OnEventWritten(EventWrittenEventArgs data)
    {
        if (!IsLogEnabled) return;
        try
        {
            Log($"HTTP event; {CurrentDescription}; name={data.EventName}; activity={data.ActivityId}; relatedActivity={data.RelatedActivityId}", Run: IsLogEnabled);
            if(data.Payload is not null)
            {
                var metadata=new[] { new KeyValuePair<string,object?>("translation",CurrentDescription) };
                Log(metadata.Concat(data.Payload.Select((value,index) => new KeyValuePair<string,object?>(data.PayloadNames?[index] ?? $"value{index}",value))),Run:IsLogEnabled);
            }
        }
        catch (Exception error) { ExceptionBoundary.LogSafely($"HTTP diagnostic logging failed; error={error}", IsLogEnabled); }
    }
    /// <summary>Preserves the engine's complete diagnostic lines without editing its source file.</summary>
    sealed class EngineOutput : TextWriter
    {
        readonly StringBuilder Buffer = new();
        public override Encoding Encoding => Encoding.UTF8;
        /// <summary>Records one complete line emitted by the unchanged translation engine.</summary>
        /// <param name="value">The original engine console line or character to record.</param>
        public override void WriteLine(string? value) => ExceptionBoundary.LogSafely($"Translation engine; {CurrentDescription}: {value}", IsLogEnabled);
        /// <summary>Buffers engine console characters until a complete diagnostic line is available.</summary>
        /// <param name="value">The original engine console line or character to record.</param>
        public override void Write(char value)
        {
            if (value == '\n') { WriteLine(Buffer.ToString()); Buffer.Clear(); }
            else if (value != '\r') Buffer.Append(value);
        }
    }
}



