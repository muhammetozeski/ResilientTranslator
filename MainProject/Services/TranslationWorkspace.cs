using Lingua;
using System.Diagnostics;
using ResilientTranslator.MainProject.Infrastructure;
using ResilientTranslator.MainProject.Infrastructure.Localization;
namespace ResilientTranslator.MainProject.Services;
/// <summary>Publishes editor results only while their request object remains the current owner.</summary>
public static class TranslationWorkspace
{
    const bool IsLogEnabled = true;
    public const string Automatic = "auto";
    internal static readonly Task<LanguageDetector> Detector = Task.Factory.StartNew(static () =>
    {
        var timer=Stopwatch.StartNew();
        Log("Offline language detector loading; languages=en,tr,de,fr,es,it,ru",Run:IsLogEnabled);
        var detector=LanguageDetectorBuilder.FromLanguages(Language.English,Language.Turkish,Language.German,Language.French,Language.Spanish,Language.Italian,Language.Russian)
            .WithLanguageModelsDirectory(Path.Combine(Path.GetDirectoryName(typeof(TranslationWorkspace).Assembly.Location)!,"Lingua","LanguageModels"))
            .WithPreloadedLanguageModels().Build();
        Log($"Offline language detector ready; elapsedMs={timer.Elapsed.TotalMilliseconds:F1}",Run:IsLogEnabled);
        return detector;
    },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
    static readonly object StateGate = new();
    static readonly SemaphoreSlim SettingsSaveGate = new(1,1);
    static RequestContext Current = new();
    static RequestContext CurrentSettingsRequest = new();
    public static RequestContext CurrentRequest => Volatile.Read(ref Current);
    public static string Input { get; private set; } = "";
    public static string Output { get; private set; } = "";
    public static string SourceLanguage { get; private set; } = "en";
    public static string TargetLanguage { get; private set; } = "tr";
    public static string LastConcreteSourceLanguage { get; private set; } = "en";
    public static string LastConcreteTargetLanguage { get; private set; } = "tr";
    public static string Status { get; private set; } = "";
    public static bool IsBusy { get; private set; }
    public static event Action? Changed;
    public static event Action? RequestSuperseded;
    public static Action? HideWindow;
    public static Action? ViewReady;
    /// <summary>Restores the portable language pair before rendering.</summary>
    public static void Initialize()
    {
        SourceLanguage=Settings.SourceLanguage.Value; TargetLanguage=Settings.TargetLanguage.Value;
        LastConcreteSourceLanguage=Settings.LastConcreteSourceLanguage.Value; LastConcreteTargetLanguage=Settings.LastConcreteTargetLanguage.Value;
        Status=Strings.Ready;
        _ = Detector.ContinueWith(task => ExceptionBoundary.LogSafely($"Offline detector initialization failed; error={task.Exception}"),CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }
    /// <summary>Replaces the owner atomically, then cancels old work outside the short state lock.</summary>
    public static void CancelPending()
    {
        RequestContext previous;
        lock (StateGate) { previous = Current; Current = new(); IsBusy = false; Status = Strings.Ready; }
        previous.Cancel(); RequestSuperseded?.Invoke(); Changed?.Invoke();
    }
    /// <summary>Accepts a new editor command or an owned captured selection and runs its translation outside the UI thread.</summary>
    /// <param name="text">The complete immutable editor snapshot.</param>
    /// <param name="fromInput">Whether the left editor is the translation source.</param>
    /// <param name="immediate">Skips the typing debounce for captured selections.</param>
    /// <param name="expectedRequest">The capture's owner object, or null for a fresh editor command.</param>
    public static async Task EditAsync(string text, bool fromInput, bool immediate = false, RequestContext? expectedRequest = null)
    {
        RequestContext previous, request;
        TranslationInput input;
        lock (StateGate)
        {
            if (expectedRequest is not null && !ReferenceEquals(expectedRequest,Current)) return;
            previous = Current; Current = request = new();
            if (fromInput) Input = text; else Output = text;
            input=new(text,fromInput ? SourceLanguage : TargetLanguage,fromInput ? TargetLanguage : SourceLanguage,
                fromInput ? Output : Input,fromInput ? LastConcreteSourceLanguage : LastConcreteTargetLanguage,fromInput ? LastConcreteTargetLanguage : LastConcreteSourceLanguage);
            IsBusy = false; Status = Strings.Ready;
        }
        previous.Cancel(); RequestSuperseded?.Invoke();
        Log($"Editor changed; request={request.Id}; fromInput={fromInput}; immediate={immediate}; text={text}", Run:IsLogEnabled);
        Changed?.Invoke();
        var outcome = await ExceptionBoundary.RunSafelyAsync(async token =>
        {
            if (!immediate) await Task.Delay(1000,token).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            lock (StateGate)
            {
                if (!ReferenceEquals(request,Current)) return;
                if (string.IsNullOrWhiteSpace(text)) { if (fromInput) Output = ""; else Input = ""; return; }
                IsBusy = true; Status = Strings.Translating;
            }
            Changed?.Invoke();
            var stopwatch = Stopwatch.StartNew();
            var job = new TranslationJob(request.Id,input,Detector);
            using var registration = token.Register(job.Cancel);
            request.Attach(job);
            try
            {
                var result = await job.Result.WaitAsync(token);
                token.ThrowIfCancellationRequested();
                lock (StateGate)
                {
                    if (!ReferenceEquals(request,Current)) return;
                    if (result.Error is not null) throw result.Error;
                    if (fromInput) Output = result.Text ?? ""; else Input = result.Text ?? "";
                    Status = $"{result.Service} · {stopwatch.Elapsed.TotalMilliseconds:0} ms";
                }
                Log($"Translation applied; request={request.Id}; text={result.Text}; service={result.Service}; elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F1}", Run:IsLogEnabled);
            }
            finally { request.Detach(job); }
        },request.Token,operationName:$"Translate-{request.Id}");
        bool refresh;
        lock (StateGate)
        {
            refresh = ReferenceEquals(request,Current);
            if (refresh) { IsBusy = false; if (outcome.Status == OperationStatus.Failed) Status = Strings.TranslationFailed; }
        }
        if (refresh) Changed?.Invoke();
    }
    /// <summary>Exchanges the two text values as one user command.</summary>
    public static void SwapTexts()
    {
        RequestContext previous;
        lock (StateGate) { previous=Current; Current=new(); (Input,Output)=(Output,Input); IsBusy=false; Status=Strings.Ready; }
        previous.Cancel(); RequestSuperseded?.Invoke(); Changed?.Invoke();
        Log("Editor texts swapped",Run:IsLogEnabled);
    }
    /// <summary>Swaps language roles and immediately translates using the new direction.</summary>
    public static Task SwapLanguagesAsync() => SetLanguagesAsync(TargetLanguage,SourceLanguage);
    /// <summary>Starts the changed translation immediately and persists settings on a separate worker.</summary>
    public static async Task SetLanguagesAsync(string source,string target)
    {
        string input;
        RequestContext previousSave,saveRequest;
        LanguagePreferences preferences;
        lock(StateGate)
        {
            bool sourceChanged=source != SourceLanguage;
            if(source==Automatic && target==Automatic)
            {
                if(sourceChanged) target=LastConcreteTargetLanguage;
                else source=LastConcreteSourceLanguage;
            }
            if(source!=Automatic) LastConcreteSourceLanguage=source;
            if(target!=Automatic) LastConcreteTargetLanguage=target;
            SourceLanguage=source; TargetLanguage=target;
            preferences=new(source,target,LastConcreteSourceLanguage,LastConcreteTargetLanguage);
            previousSave=CurrentSettingsRequest; CurrentSettingsRequest=saveRequest=new();
            input=Input;
        }
        previousSave.Cancel();
        Task translation=EditAsync(input,true,true);
        await ExceptionBoundary.RunSafelyAsync(async token =>
        {
            await SettingsSaveGate.WaitAsync(token).WaitAsync(token);
            Task<OperationResult<bool>> saving;
            try
            {
                saving=Task.Factory.StartNew(() =>
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        Settings.SourceLanguage.Value=preferences.Source;
                        Settings.TargetLanguage.Value=preferences.Target;
                        Settings.LastConcreteSourceLanguage.Value=preferences.LastSource;
                        Settings.LastConcreteTargetLanguage.Value=preferences.LastTarget;
                        return ApplicationServices.SaveSettings();
                    }
                    finally { SettingsSaveGate.Release(); }
                },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
            }
            catch { SettingsSaveGate.Release(); throw; }
            _ = saving.ContinueWith(task => ExceptionBoundary.LogSafely($"Late settings writer failure; error={task.Exception}"),CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            await saving.WaitAsync(token);
        },saveRequest.Token,operationName:"SaveLanguagePair");
        await translation;
    }
}
