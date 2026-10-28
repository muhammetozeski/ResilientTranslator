using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GTranslate.Translators; // GTranslate kütüphanesi referansı
using Polly; // Polly kütüphanesi referansı

/// <summary>
/// Provides constant string references for ISO 639-1 language codes.
/// Bypasses the performance overhead of converting enums to strings during execution.
/// </summary>
public static class Languages
{
    public const string English = "en";
    public const string Turkish = "tr";
    public const string German = "de";
    public const string French = "fr";
    public const string Spanish = "es";
    public const string Italian = "it";
    public const string Russian = "ru";
}

/// <summary>
/// Orchestrates an array of fallback translation APIs to ensure high availability and responsiveness.
/// Features a self-adjusting queue that constantly re-sorts APIs based on their real-time execution speeds.
/// Failed APIs are automatically penalized and pushed to the back of the line.
/// </summary>
public static class ResilientTranslator
{
    static readonly bool Activate = false;
    #region Configuration & State

    public const int TimeoutSeconds = 10;

    static readonly Lock ListLock = new();
    static readonly HttpClient SharedHttpClient = new();

    static readonly ITranslator GTranslateGoogle = new GoogleTranslator();
    static readonly ITranslator GTranslateGoogle2 = new GoogleTranslator2();
    static readonly ITranslator GTranslateBing = new BingTranslator();
    static readonly ITranslator GTranslateMicrosoft = new MicrosoftTranslator();
    static readonly ITranslator GTranslateYandex = new YandexTranslator();

    // API Tanımları
    static readonly TranslationApi GoogleChromeApi = new(nameof(ExecuteGoogleChromeExtAsync), "Google (clients5)", 0, ExecuteGoogleChromeExtAsync);
    static readonly TranslationApi GoogleGTranslateApi = new(nameof(ExecuteGoogleGTranslateAsync), "Google (GTranslate)", 0, ExecuteGoogleGTranslateAsync);
    static readonly TranslationApi GoogleGtxApi = new(nameof(ExecuteGoogleGtxDirectAsync), "Google (GTX)", 0, ExecuteGoogleGtxDirectAsync);
    static readonly TranslationApi GoogleGTranslateV2Api = new(nameof(ExecuteGoogleGTranslateV2Async), "Google (GTranslate v2)", 0, ExecuteGoogleGTranslateV2Async);
    static readonly TranslationApi SimplyTranslateApi = new(nameof(ExecuteSimplyTranslateAsync), "SimplyTranslate", 0, ExecuteSimplyTranslateAsync);
    static readonly TranslationApi YandexApi = new(nameof(ExecuteYandexGTranslateAsync), "Yandex (GTranslate)", 0, ExecuteYandexGTranslateAsync);
    static readonly TranslationApi MicrosoftApi = new(nameof(ExecuteMicrosoftGTranslateAsync), "Microsoft (GTranslate)", 0, ExecuteMicrosoftGTranslateAsync);
    static readonly TranslationApi BingApi = new(nameof(ExecuteBingGTranslateAsync), "Bing (GTranslate)", 0, ExecuteBingGTranslateAsync);
    static readonly TranslationApi MyMemoryApi = new(nameof(ExecuteMyMemoryAsync), "MyMemory", 500, ExecuteMyMemoryAsync);
    static readonly TranslationApi LingvaApi = new(nameof(ExecuteLingvaAsync), "Lingva", 0, ExecuteLingvaAsync);

    /// <summary>
    /// The prioritized collection of translation services. 
    /// Re-order these individual variables to change the default starting priority.
    /// </summary>
    static readonly List<TranslationApi> Apis =
    [
        GoogleChromeApi,
        GoogleGTranslateApi,
        GoogleGtxApi,
        GoogleGTranslateV2Api,
        SimplyTranslateApi,
        YandexApi,
        MicrosoftApi,
        BingApi,
        MyMemoryApi,
        LingvaApi
    ];

    #endregion

    [ModuleInitializer]
    public static void init()
    {
        if (!Activate)
            return;
        Task.Run(async () =>
        {
            await TranslateAsync("car", runConcurrently: true);
            Console.Title = "Warm Up Done";
        });
    }

    #region Core Methods

    public static async Task<(string? translatedText, Exception? Error, string UsedFunction, string UsedServiceHumanReadable)> TranslateAsync(
        string? textToTranslate,
        string sourceLanguage = Languages.English,
        string targetLanguage = Languages.Turkish,
        bool runConcurrently = false)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Log($"[TranslateAsync] İstek alındı. Kaynak Dil: {sourceLanguage}, Hedef Dil: {targetLanguage}, Eşzamanlı: {runConcurrently}", LogLevel.Debug, ConsoleColor.Cyan);

        if (string.IsNullOrWhiteSpace(textToTranslate))
        {
            Log($"[TranslateAsync] HATA: Çevrilecek metin null veya boş geldi!", LogLevel.Error, ConsoleColor.Red);
            return (null, new NullReferenceException($"{nameof(textToTranslate)} is null or white space"), string.Empty, string.Empty);
        }

        Exception? lastError = null;
        var timeoutSpan = TimeSpan.FromSeconds(TimeoutSeconds);
        var timeoutPolicy = Policy.TimeoutAsync(timeoutSpan);

        TranslationApi[] currentApis;
        lock (ListLock)
        {
            currentApis = [.. Apis];
        }

        if (runConcurrently)
        {
            Log($"[TranslateAsync] Eşzamanlı (Concurrent) mod aktif.", LogLevel.Warning, ConsoleColor.Yellow);
            using var sharedCancellationTokenSource = new CancellationTokenSource(timeoutSpan);
            var pendingTasks = new List<Task<(string? Result, TranslationApi Api, Exception? Error, Stopwatch Stopwatch)>>();

            async Task<(string? Result, TranslationApi Api, Exception? Error, Stopwatch Stopwatch)> RunApiConcurrently(TranslationApi api)
            {
                var apiStopwatch = Stopwatch.StartNew();
                try
                {
                    string? result = await timeoutPolicy.ExecuteAsync(async cancellationToken =>
                    {
                        return await api.ExecuteFunction(SharedHttpClient, sourceLanguage, targetLanguage, textToTranslate, cancellationToken).WaitAsync(timeoutSpan, cancellationToken);
                    }, sharedCancellationTokenSource.Token);

                    return (result, api, null, apiStopwatch);
                }
                catch (Exception exception)
                {
                    return (null, api, exception, apiStopwatch);
                }
            }

            foreach (var api in currentApis)
            {
                if (api.MaxCharacters > 0 && textToTranslate.Length > api.MaxCharacters) continue;
                pendingTasks.Add(RunApiConcurrently(api));
            }

            while (pendingTasks.Count > 0)
            {
                var completedTask = await Task.WhenAny(pendingTasks);
                pendingTasks.Remove(completedTask);

                var outcome = await completedTask;

                if (!string.IsNullOrWhiteSpace(outcome.Result))
                {
                    Log($"[TranslateAsync] BAŞARILI! İlk dönen API: {outcome.Api.Name}.", LogLevel.Info, ConsoleColor.Green);
                    UpdateApiPerformance(outcome.Api, outcome.Stopwatch.Elapsed, false);
                    try { sharedCancellationTokenSource.Cancel(); } catch { }
                    return (outcome.Result, null, outcome.Api.Name, outcome.Api.ServiceName);
                }
                else if (outcome.Error != null && outcome.Error is not OperationCanceledException)
                {
                    lastError = outcome.Error;
                    UpdateApiPerformance(outcome.Api, outcome.Stopwatch.Elapsed, true);
                }
            }
        }
        else
        {
            foreach (var api in currentApis)
            {
                var apiStopwatch = Stopwatch.StartNew();
                if (api.MaxCharacters > 0 && textToTranslate.Length > api.MaxCharacters) continue;

                try
                {
                    using var cancellationTokenSource = new CancellationTokenSource(timeoutSpan);
                    string? result = await timeoutPolicy.ExecuteAsync(async cancellationToken =>
                    {
                        return await api.ExecuteFunction(SharedHttpClient, sourceLanguage, targetLanguage, textToTranslate, cancellationToken).WaitAsync(timeoutSpan, cancellationToken);
                    }, cancellationTokenSource.Token);

                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        Log($"[TranslateAsync] BAŞARILI! {api.Name} sonuç döndürdü.", LogLevel.Info, ConsoleColor.Green);
                        UpdateApiPerformance(api, apiStopwatch.Elapsed, false);
                        return (result, null, api.Name, api.ServiceName);
                    }
                    UpdateApiPerformance(api, apiStopwatch.Elapsed, true);
                }
                catch (Exception exception)
                {
                    lastError = exception;
                    UpdateApiPerformance(api, apiStopwatch.Elapsed, true);
                }
            }
        }

        return (null, lastError ?? new Exception("All available translation APIs failed."), string.Empty, string.Empty);
    }

    public static async Task TranslateAsyncTestAllAPIs(
        string? textToTranslate,
        string sourceLanguage = Languages.English,
        string targetLanguage = Languages.Turkish)
    {
        Log($"[TranslateAsyncTestAllAPIs] Tüm API'ler test ediliyor.", LogLevel.Debug, ConsoleColor.Cyan);
        var summaryResults = new List<(string ApiName, string? TranslatedText, Exception? Error, double Duration)>();

        TranslationApi[] currentApis;
        lock (ListLock) { currentApis = [.. Apis]; }

        foreach (var api in currentApis)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
                string? result = await api.ExecuteFunction(SharedHttpClient, sourceLanguage, targetLanguage, textToTranslate!, cts.Token).WaitAsync(cts.Token);
                summaryResults.Add((api.Name, result, null, stopwatch.Elapsed.TotalSeconds));
            }
            catch (Exception ex) { summaryResults.Add((api.Name, null, ex, stopwatch.Elapsed.TotalSeconds)); }
        }

        Console.WriteLine("\n=== TEST ÖZETİ ===");
        foreach (var (ApiName, TranslatedText, Error, Duration) in summaryResults)
            Console.WriteLine($"{ApiName} ({(Error == null && !string.IsNullOrWhiteSpace(TranslatedText) ? "OK" : "FAIL")}) - {Duration:0.000}s");
    }

    static void UpdateApiPerformance(TranslationApi api, TimeSpan duration, bool isFailure)
    {
        lock (ListLock)
        {
            api.LastResponseTime = isFailure ? TimeSpan.MaxValue : duration;
            Apis.Sort((a, b) => a.LastResponseTime.CompareTo(b.LastResponseTime));
        }
    }

    #endregion

    #region API Execution Implementations

    // [Kütüphane Wrapper'ları]
    static Task<string?> ExecuteGoogleGTranslateAsync(HttpClient c, string s, string t, string tx, CancellationToken ct) => ExecuteLibraryTranslatorAsync(GTranslateGoogle, s, t, tx, ct);
    static Task<string?> ExecuteGoogleGTranslateV2Async(HttpClient c, string s, string t, string tx, CancellationToken ct) => ExecuteLibraryTranslatorAsync(GTranslateGoogle2, s, t, tx, ct);
    static Task<string?> ExecuteYandexGTranslateAsync(HttpClient c, string s, string t, string tx, CancellationToken ct) => ExecuteLibraryTranslatorAsync(GTranslateYandex, s, t, tx, ct);
    static Task<string?> ExecuteMicrosoftGTranslateAsync(HttpClient c, string s, string t, string tx, CancellationToken ct) => ExecuteLibraryTranslatorAsync(GTranslateMicrosoft, s, t, tx, ct);
    static Task<string?> ExecuteBingGTranslateAsync(HttpClient c, string s, string t, string tx, CancellationToken ct) => ExecuteLibraryTranslatorAsync(GTranslateBing, s, t, tx, ct);

    // [Doğrudan API Implementasyonları]
    static async Task<string?> ExecuteGoogleGtxDirectAsync(HttpClient client, string sl, string tl, string tx, CancellationToken ct)
    {
        string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={tl}&dt=t&q={Uri.EscapeDataString(tx)}";
        string json = await FetchJsonStringAsync(client, url, ct);
        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        foreach (var segment in doc.RootElement[0].EnumerateArray()) sb.Append(segment[0].GetString());
        return sb.ToString();
    }

    static async Task<string?> ExecuteLingvaAsync(HttpClient client, string sl, string tl, string tx, CancellationToken ct)
    {
        string url = $"https://lingva.ml/api/v1/{sl}/{tl}/{Uri.EscapeDataString(tx)}";
        string json = await FetchJsonStringAsync(client, url, ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("translation").GetString();
    }

    static async Task<string?> ExecuteSimplyTranslateAsync(HttpClient client, string sl, string tl, string tx, CancellationToken ct)
    {
        string url = $"https://simplytranslate.org/api/translate/?engine=google&from={sl}&to={tl}&text={Uri.EscapeDataString(tx)}";
        string json = await FetchJsonStringAsync(client, url, ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("translated_text").GetString();
    }

    static async Task<string?> ExecuteMyMemoryAsync(HttpClient client, string sl, string tl, string tx, CancellationToken ct)
    {
        string url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(tx)}&langpair={sl}|{tl}";
        string json = await FetchJsonStringAsync(client, url, ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetProperty("responseStatus").GetInt32() != 200) throw new Exception("Quota limit");
        return doc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString();
    }

    static async Task<string?> ExecuteGoogleChromeExtAsync(HttpClient client, string sl, string tl, string tx, CancellationToken ct)
    {
        string url = $"https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl={sl}&tl={tl}&q={Uri.EscapeDataString(tx)}";
        string json = await FetchJsonStringAsync(client, url, ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement[0].GetString() : doc.RootElement.GetString();
    }

    static async Task<string> FetchJsonStringAsync(HttpClient client, string url, CancellationToken ct)
    {
        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    static async Task<string?> ExecuteLibraryTranslatorAsync(ITranslator translator, string sl, string tl, string tx, CancellationToken ct)
    {
        var result = await translator.TranslateAsync(tx, tl, sl).WaitAsync(TimeSpan.FromSeconds(TimeoutSeconds), ct);
        return result.Translation;
    }

    #endregion

    #region Helpers

#pragma warning disable IDE0060 // Kullanılmayan parametreyi kaldırma
    private static void Log(string message, LogLevel level, ConsoleColor color)
#pragma warning restore IDE0060 // Kullanılmayan parametreyi kaldırma
    {
        Console.ForegroundColor = color;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        Console.ResetColor();
    }

    enum LogLevel { Debug, Info, Warning, Error }

    sealed class TranslationApi(string name, string serviceName, int maxChars, Func<HttpClient, string, string, string, CancellationToken, Task<string?>> exec)
    {
        public string Name { get; } = name;
        public string ServiceName { get; } = serviceName;
        public int MaxCharacters { get; } = maxChars;
        public Func<HttpClient, string, string, string, CancellationToken, Task<string?>> ExecuteFunction { get; } = exec;
        public TimeSpan LastResponseTime { get; set; } = TimeSpan.Zero;
    }

    #endregion
}