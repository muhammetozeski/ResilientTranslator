using Polly;
using System.Diagnostics;
using Lingua;
using ResilientTranslator.MainProject.Infrastructure;
using ResilientTranslator.MainProject.Infrastructure.Localization;
namespace ResilientTranslator.MainProject.Services;
/// <summary>Publishes editor results only while their request object remains the current owner.</summary>
public static class TranslationWorkspace
{
    const bool IsLogEnabled = true;
    public const string Automatic = TranslationLanguageRules.Automatic;
    public const string FallbackLanguage = TranslationLanguageRules.FallbackLanguage;
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


    static RequestContext Current = new();
    static RequestContext CurrentSettingsRequest = new();
    static Language LastDetectedInputLanguage = Language.Unknown;
    public static RequestContext CurrentRequest => Volatile.Read(ref Current);
    public static string Input { get; private set; } = "";
    public static string Output { get; private set; } = "";
    public static string SourceLanguage { get; private set; } = FallbackLanguage;
    public static string TargetLanguage { get; private set; } = "tr";
    public static string LastConcreteSourceLanguage { get; private set; } = FallbackLanguage;
    public static string LastConcreteTargetLanguage { get; private set; } = "tr";
    public static string Status { get; private set; } = "";
    public static bool IsBusy { get; private set; }
    public static bool HasError { get; private set; }
    public static event Action? Changed;
    public static event Action? RequestSuperseded;
    public static Action? HideWindow;
    public static Action? ViewReady;
    public static event Action? WindowHidden;
    /// <summary>Closes transient UI when the retained native surface is dismissed.</summary>
    public static void NotifyWindowHidden() => WindowHidden?.Invoke();
    public static Func<Task>? RecoverView;
    public static Func<CancellationToken,Task>? FocusInput;
    public static Func<CancellationToken,Task>? FocusWebView;
    /// <summary>Restores the language pair before rendering and observes the one-time preload.</summary>
    public static void Initialize()
    {
        SourceLanguage=Settings.DefaultSourceLanguage.Value is { Length: > 0 } source ? source : Settings.SourceLanguage.Value; TargetLanguage=Settings.DefaultTargetLanguage.Value is { Length: > 0 } target ? target : Settings.TargetLanguage.Value;
        TranslationProfiles.Initialize();
        LastConcreteSourceLanguage=Settings.LastConcreteSourceLanguage.Value; LastConcreteTargetLanguage=Settings.LastConcreteTargetLanguage.Value;
        ApplySelection(TranslationLanguageRules.NormalizeInitialization(Selection,TranslationProfiles.Routing));
        Status=Strings.Ready;
        LocManager.LanguageChanged -= RefreshLanguage;
        LocManager.LanguageChanged += RefreshLanguage;
        _ = Detector.ContinueWith(task => ExceptionBoundary.LogSafely($"Offline detector initialization failed; error={task.Exception}"),CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }
    /// <summary>Refreshes localized state messages without changing translation text or its language pair.</summary>
    static void RefreshLanguage()
    {
        lock(StateGate) { if(IsBusy) Status=Strings.Translating; else if(HasError) Status=Strings.TranslationFailed; else if(!Status.Contains(" ms")) Status=Strings.Ready; }
        Changed?.Invoke();
    }    /// <summary>Replaces the owner atomically, then cancels old work outside the short state lock.</summary>
    public static void CancelPending()
    {
        RequestContext previous;
        lock(StateGate) { previous=Current; Current=new(); IsBusy=false; HasError=false; Status=Strings.Ready; }
        previous.Cancel(); RequestSuperseded?.Invoke(); Changed?.Invoke();
    }
    /// <summary>Accepts an editor snapshot, resolves an automatic destination and starts an independent translation worker.</summary>
    /// <param name="text">The complete immutable editor snapshot.</param>
    /// <param name="fromInput">Whether the left editor is the translation source.</param>
    /// <param name="immediate">Skips the typing debounce for captured selections.</param>
    /// <param name="expectedRequest">The capture's owner object, or null for a fresh editor command.</param>
    /// <param name="forceNetwork">Skips confirmed-result reuse for this explicit user translation command.</param>
    /// <returns>Completion of this request&apos;s debounce, worker wait and ownership-checked publication.</returns>
    public static async Task EditAsync(string text,bool fromInput,bool immediate=false,RequestContext? expectedRequest=null,bool forceNetwork=false)
    {
        RequestContext previous,request;
        RequestContext? previousSave=null,saveRequest=null;
        LanguagePreferences? preferences=null;
        TranslationInput input;
        lock(StateGate)
        {
            if(expectedRequest is not null && expectedRequest != Current) return;
            previous=Current; Current=request=new();
            var selection=TranslationLanguageRules.NormalizeEdit(Selection,fromInput,!string.IsNullOrWhiteSpace(text),!string.IsNullOrWhiteSpace(Input),
                LastDetectedInputLanguage==Language.Unknown?null:LanguageInfo.IsoCode6391(LastDetectedInputLanguage).ToString().ToLowerInvariant(),TranslationProfiles.Routing);
            if(selection!=Selection){ApplySelection(selection);preferences=new(SourceLanguage,TargetLanguage,LastConcreteSourceLanguage,LastConcreteTargetLanguage);}
            if(preferences is not null) { previousSave=CurrentSettingsRequest; CurrentSettingsRequest=saveRequest=new(); }
            if(fromInput) Input=text; else Output=text;
            input=new(text,fromInput ? SourceLanguage : TargetLanguage,fromInput ? TargetLanguage : SourceLanguage,forceNetwork,TranslationProfiles.Routing,ResilientTranslator.MainProject.Infrastructure.Detection.DetectorRegistry.ResolveChoice(Settings.DetectorChoice.Value));
            IsBusy=false; HasError=false; Status=Strings.Ready;
        }
        previous.Cancel(); RequestSuperseded?.Invoke();
        if(preferences is not null && saveRequest is not null) { previousSave?.Cancel(); _=PersistPreferencesAsync(preferences,saveRequest); }
        Log($"Editor changed; request={request.Id}; fromInput={fromInput}; immediate={immediate}; source={input.SourceLanguage}; target={input.TargetLanguage}; text={text}",Run:IsLogEnabled);
        Changed?.Invoke();
        var outcome=await ExceptionBoundary.RunSafelyAsync(async token =>
        {
            if(!immediate) await Task.Delay(Settings.TypingDelay.Value,token).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            lock(StateGate)
            {
                if(request != Current) return;
                if(string.IsNullOrWhiteSpace(text)) { if(fromInput) Output=""; else Input=""; return; }
                IsBusy=true; Status=Strings.Translating;
            }
            Changed?.Invoke();
            var timer=Stopwatch.StartNew();
            var job=new TranslationJob(request.Id,input,detected =>
            {
                lock(StateGate) { if(request==Current && fromInput) LastDetectedInputLanguage=ParseLanguage(detected); }
            },update=>
            {
                lock(StateGate)
                {
                    if(request!=Current || request.Token.IsCancellationRequested)return;
                    if(fromInput)Output=update.Text;else Input=update.Text;
                    Status=$"{update.Service} · {update.ElapsedMilliseconds:0} ms";
                }
                Changed?.Invoke();
            },message=>
            {
                lock(StateGate){if(request!=Current || request.Token.IsCancellationRequested)return;Status=message;}
                Changed?.Invoke();
            });
            using var registration=token.Register(job.Cancel);
            request.Attach(job);
            try
            {
                var result=await job.Result.WaitAsync(token);
                token.ThrowIfCancellationRequested();
                lock(StateGate)
                {
                    if(request != Current) return;

                    if(fromInput) Output=result.Text; else Input=result.Text;
                    Status=$"{result.Service} · {timer.Elapsed.TotalMilliseconds:0} ms";
                }
                Log($"Translation applied; request={request.Id}; text={result.Text}; service={result.Service}; elapsedMs={timer.Elapsed.TotalMilliseconds:F1}",Run:IsLogEnabled);
            }
            finally { request.Detach(job); }
        },request.Token,operationName:$"Translate-{request.Id}");
        bool refresh;
        lock(StateGate) { refresh=request == Current; if(refresh) { IsBusy=false; if(outcome.Status==OperationStatus.Failed) { HasError=true; Status=Strings.TranslationFailed; } } }
        if(refresh) Changed?.Invoke();
    }
    /// <summary>Exchanges both texts as one command, retaining both values.</summary>
    public static void SwapTexts()
    {
        RequestContext previous;
        lock(StateGate) { previous=Current; Current=new(); (Input,Output)=(Output,Input); IsBusy=false; HasError=false; Status=Strings.Ready; }
        previous.Cancel(); RequestSuperseded?.Invoke(); Changed?.Invoke();
        Log("Editor texts swapped",Run:IsLogEnabled);
    }
    /// <summary>Exchanges the two language selections.</summary>
    /// <returns>Completion of the same normalized language-selection operation used by selectors.</returns>
    public static Task SwapLanguagesAsync() => SetLanguagesAsync(TargetLanguage,SourceLanguage);
    /// <summary>Changes language selections and persists one immutable preference snapshot.</summary>
    /// <param name="source">The source provider, language or snapshot identified by this operation.</param>
    /// <param name="target">The resolved destination language or verified payload executable.</param>
    /// <returns>Completion of language persistence and the newly selected translation operation.</returns>
    public static async Task SetLanguagesAsync(string source,string target)
    {
        string input;
        RequestContext previousSave,saveRequest;
        LanguagePreferences preferences;
        lock(StateGate)
        {
            ApplySelection(TranslationLanguageRules.NormalizeSelection(Selection,source,target,TranslationProfiles.Routing));
            source=SourceLanguage;target=TargetLanguage;
            preferences=new(source,target,LastConcreteSourceLanguage,LastConcreteTargetLanguage);
            previousSave=CurrentSettingsRequest; CurrentSettingsRequest=saveRequest=new();
            input=Input;
        }
        previousSave.Cancel();
        Task persistence=PersistPreferencesAsync(preferences,saveRequest);
        Task translation;
        if(target==Automatic && !Settings.TwoWayAutomatic.Value) { CancelPending(); translation=Task.CompletedTask; }
        else translation=EditAsync(input,true,true);
        await Task.WhenAll(persistence,translation);
    }
    /// <summary>Stores only current language preferences through the shared atomic writer.</summary>
    /// <param name="preferences">The immutable snapshot accepted by the current preference owner.</param>
    /// <param name="owner">The request object whose identity decides whether this snapshot is current.</param>
    /// <returns>The serialized preference save, or immediate completion for a superseded owner.</returns>
    static Task PersistPreferencesAsync(LanguagePreferences preferences,RequestContext owner)
    {
        lock(StateGate) return owner == CurrentSettingsRequest ? Preferences.SaveLanguagesAsync(preferences) : Task.CompletedTask;
    }
    static LanguageSelection Selection=>new(SourceLanguage,TargetLanguage,LastConcreteSourceLanguage,LastConcreteTargetLanguage);
    static void ApplySelection(LanguageSelection value){SourceLanguage=value.Source;TargetLanguage=value.Target;LastConcreteSourceLanguage=value.LastSource;LastConcreteTargetLanguage=value.LastTarget;}
    static Language ParseLanguage(string value)=>value switch{"tr"=>Language.Turkish,"de"=>Language.German,"fr"=>Language.French,"es"=>Language.Spanish,"it"=>Language.Italian,"ru"=>Language.Russian,"en"=>Language.English,_=>Language.Unknown};
    public static async Task ToggleOnlineAsync()
    {
        CancelPending();
        await Preferences.SetAsync(Settings.OnlineEnabled,(!Settings.OnlineEnabled.Value).ToString());
        await EditAsync(Input,true,true);
    }
    public static Task RestartAsync()=>EditAsync(Input,true,true);}
