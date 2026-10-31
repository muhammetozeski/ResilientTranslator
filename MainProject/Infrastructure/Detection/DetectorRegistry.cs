using System.Diagnostics;
using Lingua;
using Microsoft.JSInterop;
using ResilientTranslator.MainProject.Services;
namespace ResilientTranslator.MainProject.Infrastructure.Detection;

public enum DetectorKind { Lingua, MediaPipe, FastText, TinyLd, TinyLdLight, Cld2, Franc, Cld3 }
public sealed record DetectorDescriptor(DetectorKind Kind, string Name, long BenchmarkBytes, double MedianMs, int TurkishEnglishCorrect, int OtherCorrect)
{
    public int Correct => TurkishEnglishCorrect + OtherCorrect;
}
public sealed record DetectorOptions(int LoadTimeoutMs = 15000, int DetectTimeoutMs = 3000, int MaximumCharacters = 20000);

/// <summary>Runs the selected offline detector while retaining cancellation and diagnostic ownership.</summary>
public static class DetectorRegistry
{
    const bool IsLogEnabled = true;
    static readonly SemaphoreSlim InitializationGate = new(1, 1);
    static readonly SemaphoreSlim LinguaGate = new(1, 1);
    static IJSObjectReference? Module;
    static IJSRuntime? Runtime;
    static DetectorOptions Options = new();
    public static IReadOnlyList<DetectorDescriptor> Available { get; } = [
        new(DetectorKind.Lingua, "Lingua", 9064109, .1819, 129, 20),
        new(DetectorKind.MediaPipe, "MediaPipe", 7691631, .0676, 129, 20),
        new(DetectorKind.FastText, "fastText", 2201527, .3865, 128, 20),
        new(DetectorKind.TinyLd, "TinyLD", 591768, .0467, 122, 20),
        new(DetectorKind.TinyLdLight, "TinyLD Light", 69741, .0292, 116, 19),
        new(DetectorKind.Cld2, "CLD2 · Firefox", 1252885, .0177, 117, 16),
        new(DetectorKind.Franc, "franc-min", 121793, .0255, 101, 19),
        new(DetectorKind.Cld3, "CLD3", 1090047, .0659, 99, 14)
    ];
    public static DetectorKind ResolveChoice(string value)
    {
        if(Enum.TryParse<DetectorKind>(value,out var result)&&Enum.IsDefined(result))return result;
        ExceptionBoundary.LogSafely($"Invalid detector selection; using Lingua; value={value}",IsLogEnabled);return DetectorKind.Lingua;
    }
    public static async Task InitializeAsync(IJSRuntime js)
    {
        await InitializationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Module is { } existing && Runtime == js)
            {
                try { await existing.InvokeVoidAsync("configure", Options); return; }
                catch (Exception error) when (error is JSException or ObjectDisposedException)
                { ExceptionBoundary.LogSafely($"Offline detector bridge disconnected; recreating browser module; error={error}", IsLogEnabled); }
            }
            if (Module is { } previous)
            {
                Module = null;
                try { await previous.DisposeAsync(); }
                catch (Exception error) { ExceptionBoundary.LogSafely($"Previous detector bridge cleanup failed; error={error}", IsLogEnabled); }
            }
            var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/MainProject/detection/detection.js");
            try { await module.InvokeVoidAsync("configure", Options); }
            catch (Exception error)
            {
                try { await module.DisposeAsync(); }
                catch (Exception cleanupError) { throw new AggregateException("Detector initialization and cleanup failed.", error, cleanupError); }
                throw;
            }
            Module = module;
            Runtime = js;
            ExceptionBoundary.LogSafely("Offline detector browser bridge initialized", IsLogEnabled);
        }
        finally { InitializationGate.Release(); }
    }
    public static async Task ConfigureAsync(DetectorOptions options, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.LoadTimeoutMs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.DetectTimeoutMs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumCharacters);
        Volatile.Write(ref Options, options);
        if (Module is { } module) await module.InvokeVoidAsync("configure", token, options);
    }
    public static async Task<string?> DetectAsync(DetectorKind kind, string text, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var options = Volatile.Read(ref Options);
        var sample = text.Length <= options.MaximumCharacters ? text : text[..options.MaximumCharacters];
        var timer = Stopwatch.StartNew();
        try
        {
            string? language;
            if (kind == DetectorKind.Lingua)
            {
                language = await Task.Run(async () => {
                    var detector = await TranslationWorkspace.Detector.WaitAsync(TimeSpan.FromMilliseconds(options.LoadTimeoutMs), token).ConfigureAwait(false);
                    if(!await LinguaGate.WaitAsync(TimeSpan.FromMilliseconds(Settings.DetectorGateBudget.Value),token).ConfigureAwait(false)) throw new TimeoutException("The shared Lingua detector is occupied.");
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        var result = detector.DetectLanguageOf(sample);
                        return result == Language.Unknown ? null : LanguageInfo.IsoCode6391(result).ToString().ToLowerInvariant();
                    }
                    finally { LinguaGate.Release(); }
                }, token).WaitAsync(token).ConfigureAwait(false);
            }
            else
            {
                var module = Module ?? throw new InvalidOperationException("The offline detector bridge has not initialized.");
                var owner = new DetectionRequest(module);
                using var registration = token.Register(owner.Cancel);
                language = await module.InvokeAsync<string?>("detect", token, kind.ToString(), owner.TransportId, sample).AsTask().WaitAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            ExceptionBoundary.LogSafely($"Offline detection completed; engine={kind}; language={language}; characters={sample.Length}; elapsedMs={timer.Elapsed.TotalMilliseconds:F3}", IsLogEnabled);
            return language;
        }
        catch (Exception error)
        {
            ExceptionBoundary.LogSafely($"Offline detection failed; engine={kind}; elapsedMs={timer.Elapsed.TotalMilliseconds:F3}; error={error}", IsLogEnabled);
            throw;
        }
    }
    sealed class DetectionRequest(IJSObjectReference module)
    {
        public string TransportId { get; } = Guid.NewGuid().ToString("N");
        public void Cancel() => _ = ExceptionBoundary.RunSafelyAsync(async _ => await module.InvokeVoidAsync("cancel", TransportId), operationName: "CancelOfflineDetection");
    }
}
