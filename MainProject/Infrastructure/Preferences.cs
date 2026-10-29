using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Polly;
using ResilientTranslator.MainProject.Infrastructure.Localization;

namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Connects a reflected setting object to its editor metadata without string-based dispatch.</summary>
internal sealed record PreferenceEntry(ISetting Setting, PreferenceAttribute Editor)
{
    public string Label => (string)Editor.Resource.GetValue(null)!;
}

/// <summary>Applies typed preference edits and serializes immutable snapshots on one background writer.</summary>
public static class Preferences
{
    const bool IsLogEnabled = true;
    static readonly object StateGate = new();
    static readonly SemaphoreSlim SaveGate = new(1, 1);
    static readonly SemaphoreSlim LanguageGate = new(1, 1);
    internal static readonly PreferenceEntry[] Entries = typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (Field: field, Editor: field.GetCustomAttribute<PreferenceAttribute>()))
        .Where(item => item.Editor is not null)
        .Select(item => new PreferenceEntry((ISetting)item.Field.GetValue(null)!, item.Editor!)).ToArray();
    public static event Action? Changed;
    public static Func<Task<string[]>>? LoadFonts;
    public static double InputShare => Settings.InputShare.Value;
    public static int CenterSnapRange => Settings.CenterSnapRange.Value;
    public static double StatusSpeed => Settings.StatusSpeed.Value;
    public static int WindowWidth => Settings.WindowWidth.Value;
    public static int WindowHeight => Settings.WindowHeight.Value;
    public static int ClipboardChangeBudget => Settings.ClipboardChangeBudget.Value;
    public static string SaveError { get; private set; } = "";
    public static string EditorFontFamily => Settings.EditorFont.Value;
    public static double EditorFontSize => Settings.EditorFontSize.Value;

    /// <summary>Rejects invalid persisted limits before any window or worker consumes them.</summary>
    public static void Initialize()
    {
        foreach (var entry in Entries)
            if (!IsValid(entry.Setting, entry.Editor))
            {
                Log($"Invalid persisted preference reset; key={entry.Setting.Key}; value={entry.Setting.Value}", Run: IsLogEnabled);
                entry.Setting.ResetToDefault();
            }
        ThemeManager.Apply(ThemeManager.Current with { EditorText = Color.FromRgba(Settings.EditorColor.Value) });
        if (Settings.WindowWidth.Value < 480) Settings.WindowWidth.ResetToDefault();
        if (Settings.WindowHeight.Value < 280) Settings.WindowHeight.ResetToDefault();
    }

    /// <summary>Validates a UI edit against its reflected metadata and saves accepted values.</summary>
    /// <param name="setting">The registered setting object.</param>
    /// <param name="text">Invariant serialized UI value.</param>
    /// <returns>Whether parsing, validation and persistence succeeded.</returns>
    internal static async Task<bool> SetAsync(ISetting setting, string text)
    {
        if (setting == Settings.Language) return await SetLanguageAsync(text);
        lock (StateGate)
        {
            object? previous = setting.Value;
            var entry = Entries.Single(item => item.Setting == setting);
            if (!setting.TrySetFromText(text)) return false;
            if (!IsValid(setting, entry.Editor)) { setting.Value = previous; return false; }
            Logger.ActivateLogging = Settings.EnableLogging.Value;
        }
        if (setting == Settings.EditorColor) ThemeManager.Apply(ThemeManager.Current with { EditorText = Color.FromRgba(Settings.EditorColor.Value) });
        Log($"Preference changed; key={setting.Key}; value={setting.Value}", Run: IsLogEnabled);
        Changed?.Invoke();
        return await SaveAsync();
    }

    /// <summary>Serializes complete UI-language changes off the dispatcher and persists only successful selections.</summary>
    static async Task<bool> SetLanguageAsync(string code)
    {
        if (!LanguageCatalog.All.Any(language => language.Code == code && code != "auto")) return false;
        await LanguageGate.WaitAsync();
        try
        {
            var outcome = await Task.Run(() => ApplicationServices.ChangeLanguage(code, persist: false));
            if (!outcome.Succeeded) return false;
            lock (StateGate) Settings.Language.Value = code;
            Changed?.Invoke();
            return await SaveAsync();
        }
        finally { LanguageGate.Release(); }
    }

    /// <summary>Keeps resolved language history separate from startup defaults.</summary>
    internal static Task SaveLanguagesAsync(LanguagePreferences languages)
    {
        lock (StateGate)
        {
            Settings.SourceLanguage.Value = languages.Source;
            Settings.TargetLanguage.Value = languages.Target;
            Settings.LastConcreteSourceLanguage.Value = languages.LastSource;
            Settings.LastConcreteTargetLanguage.Value = languages.LastTarget;
        }
        return SaveAsync();
    }

    /// <summary>Stores the last completed resize, with no file access on the window dispatcher.</summary>
    /// <param name="width">Native window width in pixels.</param>
    /// <param name="height">Native window height in pixels.</param>
    public static Task SaveWindowSizeAsync(int width, int height)
    {
        lock (StateGate) { Settings.WindowWidth.Value = width; Settings.WindowHeight.Value = height; }
        return SaveAsync();
    }

    /// <summary>Writes the newest complete state after acquiring the writer; later edits cannot be overwritten by older snapshots.</summary>
    /// <returns>Whether the atomic write succeeded after transient retries.</returns>
    public static async Task<bool> SaveAsync()
    {
        var outcome = await ExceptionBoundary.RunSafelyAsync(async token =>
        {
            await SaveGate.WaitAsync(token).WaitAsync(token);
            try
            {
                string snapshot;
                lock (StateGate) snapshot = SettingsManager.CaptureSnapshot();
                await Task.Run(() => Policy.Handle<IOException>().Or<UnauthorizedAccessException>()
                    .WaitAndRetry(3, attempt => TimeSpan.FromMilliseconds(25 * attempt),
                        (error, delay, attempt, context) => ExceptionBoundary.LogSafely($"Settings write retry; attempt={attempt}; error={error}"))
                    .Execute(() => SettingsManager.SaveSnapshot(snapshot, AppConstants.ConfigFileName))).ConfigureAwait(false);
                SaveError = "";
                Log($"Preferences saved; path={AppConstants.ConfigFileName}", Run: IsLogEnabled);
            }
            finally { SaveGate.Release(); }
        }, operationName: "SavePreferences");
        if (!outcome.Succeeded) { SaveError = Strings.SettingsSaveFailed; Changed?.Invoke(); }
        return outcome.Succeeded;
    }

    /// <summary>Checks semantic limits in addition to the registered type's parser.</summary>
    static bool IsValid(ISetting setting, PreferenceAttribute metadata)
    {
        if (setting.Value is double or int)
        {
            double number = Convert.ToDouble(setting.Value, CultureInfo.InvariantCulture);
            if (!double.IsFinite(number) || number < metadata.Minimum || number > metadata.Maximum) return false;
        }
        if (setting == Settings.EditorColor) return Settings.EditorColor.Value is { } color && Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$");
        if (setting == Settings.DefaultSourceLanguage || setting == Settings.DefaultTargetLanguage)
            return setting.Value is string code && (code == "" || LanguageCatalog.All.Any(language => language.Code == code));
        return setting != Settings.EditorFont || !string.IsNullOrWhiteSpace(Settings.EditorFont.Value);
    }
}
