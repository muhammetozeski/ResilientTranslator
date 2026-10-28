using System.Diagnostics.Tracing;
using System.Text;
namespace ResilientTranslator.MainProject.Infrastructure;
/// <summary>Routes the unchanged engine's console output and HTTP lifecycle events into the portable logger.</summary>
public sealed class EngineTrace : EventListener
{
    const bool IsLogEnabled = true;
    static EngineTrace? Listener;
    static readonly AsyncLocal<TranslationTrace?> CurrentTrace = new();
    internal static long? RequestId => CurrentTrace.Value?.Request;
    internal static string CurrentDescription => CurrentTrace.Value is { } trace ? $"request={trace.Request}; attempt={trace.Attempt}; mode={trace.Mode}" : "request=none";
    /// <summary>Flows diagnostic correlation through the original engine's asynchronous HTTP calls.</summary>
    internal static IDisposable Enter(long request,int attempt,TranslationMode mode)
    {
        var previous=CurrentTrace.Value;
        CurrentTrace.Value=new(request,attempt,mode);
        return new TraceScope(previous);
    }
    sealed record TranslationTrace(long Request,int Attempt,TranslationMode Mode);
    sealed class TraceScope(TranslationTrace? previous) : IDisposable
    {
        bool Disposed;
        public void Dispose() { if(!Disposed) { Disposed=true; CurrentTrace.Value=previous; } }
    }
    /// <summary>Installs process diagnostics before translation providers are initialized.</summary>
    public static void Initialize()
    {
        Listener ??= new EngineTrace();
        Console.SetOut(TextWriter.Synchronized(new EngineOutput()));
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ExceptionBoundary.LogSafely($"Unhandled exception; terminating={args.IsTerminating}; error={args.ExceptionObject}", IsLogEnabled, waitForLogging: true);
        TaskScheduler.UnobservedTaskException += (_, args) => { ExceptionBoundary.LogSafely($"Unobserved task exception; error={args.Exception}", IsLogEnabled); args.SetObserved(); };
        ExceptionBoundary.LogSafely("Engine console and HTTP diagnostics attached", IsLogEnabled);
    }
    /// <summary>Enables the runtime's HTTP request, response and failure events.</summary>
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "System.Net.Http") EnableEvents(source, EventLevel.Informational);
    }
    /// <summary>Records network operation names and every payload field supplied by the runtime.</summary>
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
        public override void WriteLine(string? value) => ExceptionBoundary.LogSafely($"Translation engine; {CurrentDescription}: {value}", IsLogEnabled);
        public override void Write(char value)
        {
            if (value == '\n') { WriteLine(Buffer.ToString()); Buffer.Clear(); }
            else if (value != '\r') Buffer.Append(value);
        }
    }
}



