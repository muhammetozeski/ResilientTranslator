using System.Diagnostics;
using System.ComponentModel;
using ResilientTranslator.MainProject.Constants;
using ResilientTranslator.MainProject.Infrastructure;

/// <summary>Starts the AppData payload, forwarding arguments unchanged and sharing the existing diagnostic sources.</summary>
internal static class LauncherProgram
{
    const bool IsLogEnabled = true;
    /// <summary>Starts the native payload before asynchronously draining launcher diagnostics.</summary>
    /// <param name="args">Arguments forwarded in their original order and values.</param>
    /// <returns>Zero after successful process creation, or one after the logged final failure.</returns>
    static async Task<int> Main(string[] args)
    {
        string target = Path.Combine(AppContext.BaseDirectory,AppConstants.PayloadFolderName,AppConstants.AppName + ".exe");
        var outcome = ExceptionBoundary.RunSafely(() =>
        {
            if (!File.Exists(target)) throw new FileNotFoundException("The portable application payload is missing.",target);
            Log($"Payload launch requested; target={target}; argumentCount={args.Length}",Run:IsLogEnabled);
            int processId;
            try { processId = Start(target,args,false); }
            catch (Exception directError) when (directError is Win32Exception or InvalidOperationException)
            {
                ExceptionBoundary.LogSafely($"Direct payload launch failed; trying shell process creation; error={directError}",IsLogEnabled);
                try { processId = Start(target,args,true); }
                catch (Exception shellError) { throw new AggregateException("Both payload launch methods failed.",directError,shellError); }
            }
            ExceptionBoundary.LogSafely($"Payload process created; processId={processId}",IsLogEnabled);
        },operationName:"LaunchPayload");
        try { if (!await Logger.CompleteAsync("Launcher handoff diagnostics complete")) return 1; }
        catch (Exception flushError) { ExceptionBoundary.LogSafely($"Launcher diagnostic flush failed; error={flushError}",IsLogEnabled); return 1; }
        return outcome.Succeeded ? 0 : 1;
    }
    /// <summary>Creates one payload process without concatenating or quoting its arguments.</summary>
    /// <param name="target">The verified application executable.</param>
    /// <param name="args">The original immutable argument sequence.</param>
    /// <param name="useShell">Whether the fallback should use Windows shell process creation.</param>
    /// <returns>The created process identifier; no log failure retries process creation.</returns>
    static int Start(string target,string[] args,bool useShell)
    {
        var start = new ProcessStartInfo(target) { UseShellExecute=useShell,WorkingDirectory=Path.GetDirectoryName(target)! };
        foreach (string argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Process creation returned no payload handle.");
        return process.Id;
    }
}
