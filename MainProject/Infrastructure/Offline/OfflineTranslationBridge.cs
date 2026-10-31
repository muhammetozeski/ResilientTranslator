using System.Diagnostics;
using Microsoft.JSInterop;

namespace ResilientTranslator.MainProject.Infrastructure.Offline;

/// <summary>The supplied local inference engines; ownership is held by invocation objects, not their diagnostic numbers.</summary>
public enum OfflineEngine { Bergamot, Toz }

/// <summary>Controls retained worker capacity and the original engines' supported inference options.</summary>
public sealed record OfflineEngineOptions
{
    public int BergamotWorkers { get; init; } = 1;
    public int TozWorkers { get; init; } = 1;
    public int TozThreads { get; init; } = 1;
    public int BergamotBeam { get; init; } = 4;
    public int BergamotCachedDirections { get; init; } = 2;
    public int TozBeam { get; init; } = 4;
    public int ModelLoadTimeoutMs { get; init; } = 60000;
    public int TranslationTimeoutMs { get; init; } = 30000;
}

/// <summary>Calls the supplied browser inference engines without a Node process or JavaScript source embedded in C#.</summary>
public static class OfflineTranslationBridge
{
    const bool IsLogEnabled = true;
    static readonly SemaphoreSlim InitializationGate = new(1, 1);
    static TaskCompletionSource<IJSObjectReference> Available = new(TaskCreationOptions.RunContinuationsAsynchronously);
    static readonly DotNetObjectReference<BridgeCallbacks> Callbacks = DotNetObjectReference.Create(new BridgeCallbacks());
    static IJSObjectReference? Module;
    static IJSRuntime? Runtime;
    static OfflineEngineOptions Options = new();
    static long NextInvocation;

    /// <summary>Connects the retained WebView to the module; actual inference stays inside terminable Web Workers.</summary>
    public static async Task InitializeAsync(IJSRuntime javascript)
    {
        await InitializationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Module is not null && Runtime == javascript) return;
            if (Module is { } previous)
            {
                Available = new(TaskCreationOptions.RunContinuationsAsynchronously);
                try { await previous.DisposeAsync(); }
                catch (Exception error) { ExceptionBoundary.LogSafely($"Previous offline bridge disposal failed during renderer recovery; error={error}", IsLogEnabled); }
            }
            var module = await javascript.InvokeAsync<IJSObjectReference>("import", "./_content/MainProject/offline/bridge.js");
            await module.InvokeVoidAsync("initialize", Options, Callbacks);
            Module = module;
            Runtime = javascript;
            Available.TrySetResult(module);
            Log("Offline translation bridge initialized", Run: IsLogEnabled);
        }
        finally { InitializationGate.Release(); }
    }

    /// <summary>Applies bounded engine settings. Changed worker settings retire old workers before replacement.</summary>
    public static async Task ConfigureAsync(OfflineEngineOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BergamotWorkers is < 1 or > 8 || options.TozWorkers is < 1 or > 8 || options.TozThreads is < 1 or > 16 || options.BergamotBeam is < 1 or > 8 || options.BergamotCachedDirections is < 1 or > 2 || options.TozBeam is < 1 or > 8 || options.ModelLoadTimeoutMs < 1000 || options.TranslationTimeoutMs < 1000)
            throw new ArgumentOutOfRangeException(nameof(options));
        Options = options;
        if (Module is { } module) await module.InvokeVoidAsync("configure", cancellationToken, options);
    }

    /// <summary>Preloads the currently selected directions while the rendered application is hidden.</summary>
    public static async Task WarmAsync(string source, string target, CancellationToken cancellationToken = default)
    {
        var module = await Available.Task.WaitAsync(TimeSpan.FromMilliseconds(Options.ModelLoadTimeoutMs), cancellationToken).ConfigureAwait(false);
        var invocation = new Invocation(Interlocked.Increment(ref NextInvocation), OfflineEngine.Bergamot);
        await module.InvokeVoidAsync("start", invocation.Number);
        try
        {
            using var registration = cancellationToken.Register(() => _ = CancelAsync(module, invocation));
            cancellationToken.ThrowIfCancellationRequested();
            await module.InvokeVoidAsync("warm", invocation.Number, source, target).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { await CancelAsync(module, invocation).ConfigureAwait(false); }
    }

    /// <summary>Translates through the original models, composing a Bergamot English leg when Töz handles a Turkish pair.</summary>
    public static async Task<string> TranslateAsync(OfflineEngine engine, string text, string source, string target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (source == target) return text;
        var invocation = new Invocation(Interlocked.Increment(ref NextInvocation), engine);
        var timer = Stopwatch.StartNew();
        var module = await Available.Task.WaitAsync(TimeSpan.FromMilliseconds(Options.ModelLoadTimeoutMs), cancellationToken).ConfigureAwait(false);
        // The acknowledgement makes cancellation safe even when it arrives before inference is dispatched.
        await module.InvokeVoidAsync("start", invocation.Number);
        try
        {
            using var registration = cancellationToken.Register(() => _ = CancelAsync(module, invocation));
            cancellationToken.ThrowIfCancellationRequested();
            Log($"Offline translation started; invocation={invocation.Number}; engine={engine}; source={source}; target={target}; text={text}", Run: IsLogEnabled);
            string result = await module.InvokeAsync<string>("translate", invocation.Number, engine.ToString(), text, source, target).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(result)) throw new InvalidDataException($"{engine} returned no translation.");
            Log($"Offline translation completed; invocation={invocation.Number}; engine={engine}; elapsedMs={timer.Elapsed.TotalMilliseconds:F1}; text={result}", Run: IsLogEnabled);
            return result;
        }
        catch (Exception error)
        {
            Log($"Offline translation failed; invocation={invocation.Number}; engine={engine}; elapsedMs={timer.Elapsed.TotalMilliseconds:F1}; error={error}", Run: IsLogEnabled);
            throw;
        }
        finally { await CancelAsync(module, invocation).ConfigureAwait(false); }
    }

    static async Task CancelAsync(IJSObjectReference module, Invocation invocation)
    {
        try { await module.InvokeVoidAsync("cancel", invocation.Number); }
        catch (Exception error) { ExceptionBoundary.LogSafely($"Offline worker cancellation failed; invocation={invocation.Number}; error={error}", IsLogEnabled); }
    }

    sealed record Invocation(long Number, OfflineEngine Engine);

    /// <summary>Routes the engine's initialization and error diagnostics to the application's existing log.</summary>
    public sealed class BridgeCallbacks
    {
        const bool IsLogEnabled = true;
        [JSInvokable]
        public void Report(string level, string message) => Log($"Offline browser engine; level={level}; {message}", Run: IsLogEnabled);
    }
}
