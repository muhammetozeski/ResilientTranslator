using System.Diagnostics.Tracing;
using System.Text;
namespace ResilientTranslator.MainProject.Infrastructure;
/// <summary>Routes the unchanged engine's console output and HTTP lifecycle events into the portable logger.</summary>
public sealed class EngineTrace : EventListener
{
    const bool IsLogEnabled = true;
    static EngineTrace? Listener;
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
            Log($"HTTP event; name={data.EventName}; activity={data.ActivityId}; relatedActivity={data.RelatedActivityId}", Run: IsLogEnabled);
            if (data.Payload is not null) Log(data.PayloadNames is null ? data.Payload : data.PayloadNames.Zip(data.Payload, static (name, value) => new KeyValuePair<string,object?>(name,value)), Run: IsLogEnabled);
        }
        catch (Exception error) { ExceptionBoundary.LogSafely($"HTTP diagnostic logging failed; error={error}", IsLogEnabled); }
    }
    /// <summary>Preserves the engine's complete diagnostic lines without editing its source file.</summary>
    sealed class EngineOutput : TextWriter
    {
        readonly StringBuilder Buffer = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) => ExceptionBoundary.LogSafely($"Translation engine: {value}", IsLogEnabled);
        public override void Write(char value)
        {
            if (value == '\n') { WriteLine(Buffer.ToString()); Buffer.Clear(); }
            else if (value != '\r') Buffer.Append(value);
        }
    }
}

