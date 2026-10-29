using System.Reflection;
using System.Xml.Linq;
using System.Xml;
using Polly;
using ResilientTranslator.MainProject.Infrastructure.Localization;
using static ResilientTranslator.MainProject.Infrastructure.ExceptionBoundary;

namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Connects settings and language files at the application entry points without rewriting their engines.</summary>
public static class ApplicationServices
{
    const bool IsLogEnabled = true;
    static readonly object InitializationLock = new();
    static bool Initialized;
    static readonly object LanguageLock = new();

    /// <summary>Loads portable settings and prepares all shipped languages once per process.</summary>
    /// <returns>The final initialization outcome; failures have already been logged and contained.</returns>
    public static OperationResult<bool> Initialize() => RunSafely(() =>
    {
        lock (InitializationLock)
        {
            if (Initialized)
                return;

            Directory.CreateDirectory(AppPaths.UserData);
            SettingsPersistence.Load();
            Logger.ActivateLogging = Settings.EnableLogging.Value;
            LocManager.Init(Settings.Language.Value);
            SettingsManagerSettings.IsTurkish = LocManager.Current == "tr";
            foreach(var language in LanguageCatalog.All.Where(item => item.Code != "auto")) ValidateLanguageFile(language.Code);
            ValidateAppliedLanguage(LocManager.Resolve(Settings.Language.Value));
            Initialized = true;
            if (IsLogEnabled)
                Log($"Application initialized; root={AppPaths.Root}; language={LocManager.Current}; settings={SettingsManager.GetAllSettings().Length}", Run: IsLogEnabled);
        }
    });

    /// <summary>Saves the registered settings through the common final failure boundary.</summary>
    /// <returns>The save outcome; the caller must check Succeeded before displaying success.</returns>
    public static OperationResult<bool> SaveSettings() => RunSafely(() =>
    {
        Directory.CreateDirectory(AppPaths.UserData);
        SettingsPersistence.SaveSnapshot(SettingsManager.CaptureSnapshot());
        Logger.ActivateLogging = Settings.EnableLogging.Value;
        if (IsLogEnabled)
            Log($"Settings saved; path={ConfigFileName}; count={SettingsManager.GetAllSettings().Length}", Run: IsLogEnabled);
    });

    /// <summary>Applies a complete language file and optionally saves the preference for the next launch.</summary>
    /// <param name="language">A language code or system to follow the operating system language.</param>
    /// <param name="persist">Whether the selection should be stored in portable settings.</param>
    /// <returns>The applied language code or the contained failure.</returns>
    public static OperationResult<string> ChangeLanguage(string language, bool persist = true) => RunSafely(() =>
    {
        lock (LanguageLock)
        {
            string previousLanguage = LocManager.Current;
            string previousSelection = Settings.Language.Value;
            var previousValues = LocManager.CaptureValues();
            try
            {
                string resolvedLanguage = LocManager.Resolve(language);
                ValidateLanguageFile(resolvedLanguage);
                Policy.Handle<InvalidDataException>().Retry(1,(error,attempt) => Log($"Retrying complete language apply; language={resolvedLanguage}; error={error}",Run:IsLogEnabled))
                    .Execute(() => { LocManager.Apply(language); ValidateAppliedLanguage(resolvedLanguage); });
                SettingsManagerSettings.IsTurkish = LocManager.Current == "tr";
                if (persist)
                {
                    Settings.Language.Value = language;
                    SettingsPersistence.SaveSnapshot(SettingsManager.CaptureSnapshot());
                }
                Log($"Language changed; selection={language}; active={LocManager.Current}; persisted={persist}", Run:IsLogEnabled);
                return LocManager.Current;
            }
            catch(Exception applyError)
            {
                try
                {
                    Settings.Language.Value = previousSelection;
                    LocManager.RestoreValues(previousLanguage,previousValues);
                    SettingsManagerSettings.IsTurkish = previousLanguage == "tr";
                }
                catch(Exception rollbackError) { throw new AggregateException("Language apply and rollback failed.",applyError,rollbackError); }
                throw;
            }
        }    });

    /// <summary>Validates a language, repairing malformed packaged files through the embedded original.</summary>
    /// <param name="language">The resolved UI culture code.</param>
    /// <returns>A complete resource dictionary, or the aggregate failure after repair.</returns>
    static Dictionary<string, string> ValidateLanguageFile(string language)
    {
        try { return ReadLanguageWithRetry(language); }
        catch (Exception inputError) when (inputError is XmlException or InvalidDataException or ArgumentException or FileNotFoundException)
        {
            if (!LanguageCatalog.All.Any(item => item.Code == language && item.Code != "auto")) throw;
            Log($"UI language validation failed; restoring shipped resource; language={language}; error={inputError}", Run:IsLogEnabled);
            try { LocManager.RestoreShippedFile(language); return ReadLanguageWithRetry(language); }
            catch (Exception repairError) { throw new AggregateException("UI language validation and repair failed.", inputError, repairError); }
        }
    }
    /// <summary>Retries only transient file reads before selecting resource repair.</summary>
    /// <param name="language">The resolved UI culture code or source language selected by the operation.</param>
    /// <returns>A complete language dictionary after transient read retries.</returns>
    static Dictionary<string,string> ReadLanguageWithRetry(string language) => Policy.Handle<IOException>()
        .WaitAndRetry(2, attempt => TimeSpan.FromMilliseconds(20 * attempt),
            (error, delay, attempt, context) => Log($"UI language read retry; language={language}; attempt={attempt}; error={error}",Run:IsLogEnabled))
        .Execute(() => ReadLanguageFile(language));
    /// <summary>Rejects incomplete language files before they can leave a mix of old and new strings.</summary>
    /// <param name="language">The resolved culture code.</param>
    /// <returns>The language values indexed by field name.</returns>
    static Dictionary<string, string> ReadLanguageFile(string language)
    {
        _ = System.Globalization.CultureInfo.GetCultureInfo(language);
        string path = Path.Combine(AppPaths.UserData, $"lang.{language}.xml");
        var document = XDocument.Load(path);
        if (document.Root?.Name != "strings")
            throw new InvalidDataException($"Invalid language document: {path}");
        var values = document.Root.Elements("s").ToDictionary(
            element => element.Attribute("name")?.Value ?? throw new InvalidDataException($"A language entry has no name: {path}"),
            element => element.Value.Replace("\\n", "\n"), StringComparer.Ordinal);
        foreach (var field in typeof(Strings).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.FieldType == typeof(string) && !values.ContainsKey(field.Name))
                throw new InvalidDataException($"Language {language} is missing {field.Name}.");
        return values;
    }

    /// <summary>Checks the result because the preserved localization engine contains some failures internally.</summary>
    /// <param name="language">The language expected after the engine returns.</param>
    static void ValidateAppliedLanguage(string language)
    {
        var values = ValidateLanguageFile(language);
        if (LocManager.Current != language)
            throw new InvalidDataException($"Expected language {language}, received {LocManager.Current}.");
        foreach (var field in typeof(Strings).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.FieldType == typeof(string) && !Equals(field.GetValue(null), values[field.Name]))
                throw new InvalidDataException($"Language {language} was not applied to {field.Name}.");
    }
}



