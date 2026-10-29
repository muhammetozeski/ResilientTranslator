using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using ResilientTranslator.MainProject.Infrastructure;

namespace ResilientTranslator.MainProject.Infrastructure.Localization;

/// <summary>
/// Loads seven embedded UI languages into portable UserData files and applies their
/// values to Strings through reflection. Version or key changes preserve the previous
/// file before refreshing it from the shipped resource.
///
/// Dropping another <c>lang.&lt;code&gt;.xml</c> into the configuration folder is enough to add a
/// language; it shows up in the picker without a code change.
/// </summary>
public static class LocManager
{
    internal const bool IsLogEnabled = true;
    public const string SystemLanguage = "system";
    private const string ResourcePrefix = "ResilientTranslator.MainProject.Infrastructure.Localization.lang.";

    /// <summary>Fires after the active language changes so open windows can re-apply their text.</summary>
    public static event Action? LanguageChanged;

    public static string Current { get; private set; } = "tr";

    private static string Folder => AppPaths.UserData;

    public static (string Code, string Name)[] Available
    {
        get
        {
            try
            {
                if (!Directory.Exists(Folder))
                {
                    return [("tr", "Türkçe")];
                }

                var found = Directory.GetFiles(Folder, "lang.*.xml")
                    .Select(file =>
                    {
                        var parts = Path.GetFileName(file).Split('.');
                        var code = parts.Length > 1 ? parts[1] : "??";
                        var name = code.ToUpperInvariant();

                        try
                        {
                            var doc = XDocument.Load(file);
                            var languageName = doc.Root?.Element("LanguageName")?.Value;
                            if (!string.IsNullOrWhiteSpace(languageName))
                            {
                                name = languageName;
                            }
                        }
                        catch (Exception ex)
                        {
                            LocalizationLog.App($"Could not read the language name from {Path.GetFileName(file)}: {ex.GetType().Name}");
                        }

                        return (Code: code, Name: name);
                    })
                    .OrderBy(item => item.Name, StringComparer.CurrentCulture)
                    .ToArray();

                return found.Length > 0 ? found : [("tr", "Türkçe")];
            }
            catch (Exception ex)
            {
                LocalizationLog.Error("Could not list the available languages", ex);
                return [("tr", "Türkçe")];
            }
        }
    }

    /// <summary>Selects the mutable public string fields shared by every UI resource file.</summary>
    /// <returns>Reflection descriptors for mutable public UI strings.</returns>
    private static FieldInfo[] StringFields() => typeof(Strings)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(string))
        .ToArray();

    /// <summary>Resolves one UI-language file inside the portable UserData directory.</summary>
    /// <param name="language">The resolved UI culture code or source language selected by the operation.</param>
    /// <returns>The portable XML path for the requested UI language.</returns>
    private static string PathFor(string language) => Path.Combine(Folder, $"lang.{language}.xml");

    /// <summary>
    /// Prepares the language files and applies the configured language. Pass "system" to follow the
    /// Windows display language, which is what a first run does.
    /// </summary>
    /// <param name="configuredLanguage">The stored explicit UI culture or system-language selection.</param>
    public static void Init(string configuredLanguage)
    {
        try
        {
            Directory.CreateDirectory(Folder);

            // The shipped files are rewritten when the application version changes.
            //
            // They are written once and then loaded over the compiled-in text, so without this a
            // wording change in a new build never reaches the screen: the stale file wins and the
            // interface silently keeps the old version's text. A copy of the previous file is kept
            // alongside so hand edits are not simply thrown away.
            var currentVersion = AppPaths.PayloadVersion;

            foreach (var language in LanguageCatalog.All.Where(item => item.Code != "auto"))
                RefreshShippedFile(PathFor(language.Code), currentVersion, () => ExtractResource(language.Code, currentVersion));
        }
        catch (Exception ex)
        {
            LocalizationLog.Error("Preparing the language files failed", ex);
        }

        Apply(configuredLanguage);
    }

    /// <summary>Refreshes a missing or outdated UI-language resource after preserving its previous user-editable file.</summary>
    /// <param name="path">The portable text/resource path selected by this operation.</param>
    /// <param name="currentVersion">The executable version stamped on the shipped resource copy.</param>
    /// <param name="write">Writes the selected shipped resource after version/key validation.</param>
    private static void RefreshShippedFile(string path, string currentVersion, Action write)
    {
        try
        {
            if (!File.Exists(path))
            {
                write();
                return;
            }

            var stamped = ReadVersion(path);
            if (stamped == currentVersion)
            {
                var keys = XDocument.Load(path).Root?.Elements("s").Select(element => (string?)element.Attribute("name")).ToHashSet();
                if (keys is not null && StringFields().All(field => keys.Contains(field.Name))) return;
            }

            var backup = path + ".previous";
            File.Copy(path, backup, overwrite: true);
            LocalizationLog.App($"{Path.GetFileName(path)} was written by version {stamped ?? "unknown"}; refreshing it and keeping the old one as {Path.GetFileName(backup)}");

            write();
        }
        catch (Exception ex)
        {
            LocalizationLog.Error($"Could not refresh {Path.GetFileName(path)}", ex);
        }
    }

    /// <summary>Reads the optional executable-version stamp used to refresh a shipped language.</summary>
    /// <param name="path">The portable text/resource path selected by this operation.</param>
    /// <returns>The saved language version, or null when it cannot be read.</returns>
    private static string? ReadVersion(string path)
    {
        try
        {
            return XDocument.Load(path).Root?.Element("Version")?.Value;
        }
        catch (Exception ex)
        {
            LocalizationLog.App($"Could not read the version stamp from {Path.GetFileName(path)}: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>Loads a language over the <see cref="Strings"/> fields and notifies listeners.</summary>
    /// <param name="configuredLanguage">The stored explicit UI culture or system-language selection.</param>
    public static void Apply(string configuredLanguage)
    {
        var language = Resolve(configuredLanguage);

        try
        {
            if (language == "tr")
            {
                // The fields may hold another language after a switch, and lang.tr.xml is editable,
                // so it is read back whenever it exists. Skipping it would keep the previous
                // language on screen and silently discard the user's own wording.
                var turkishPath = PathFor("tr");
                if (File.Exists(turkishPath))
                {
                    ReadInto(turkishPath);
                }
            }
            else
            {
                var path = PathFor(language);
                if (File.Exists(path))
                {
                    ReadInto(path);
                }
                else
                {
                    LocalizationLog.App($"Language file not found: {path}; keeping the Turkish defaults.");
                    language = "tr";
                }
            }

            Current = language;

            var culture = new CultureInfo(language);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (Exception ex)
        {
            LocalizationLog.Error($"Applying the language '{language}' failed", ex);
        }

        try
        {
            LanguageChanged?.Invoke();
        }
        catch (Exception ex)
        {
            LocalizationLog.Error("A LanguageChanged handler threw", ex);
        }
    }

    /// <summary>Turns "system" into a concrete language code against the Windows display language.</summary>
    /// <param name="configuredLanguage">The stored explicit UI culture or system-language selection.</param>
    /// <returns>The concrete UI culture code selected from explicit or system settings.</returns>
    public static string Resolve(string configuredLanguage)
    {
        var configured = (configuredLanguage ?? string.Empty).Trim().ToLowerInvariant();

        if (configured.Length > 0 && configured != SystemLanguage)
        {
            return configured;
        }

        try
        {
            var system = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            if (File.Exists(PathFor(system)))
            {
                return system;
            }

            // A first run happens before the files are written, so fall back to the two languages
            // that always exist.
            if (system is "tr" or "en")
            {
                return system;
            }
        }
        catch (Exception ex)
        {
            LocalizationLog.Error("Could not read the Windows display language", ex);
        }

        return "en";
    }

    /// <summary>Reflection-loads values from an XML file over the matching fields.</summary>
    /// <param name="path">The portable text/resource path selected by this operation.</param>
    private static void ReadInto(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            var map = StringFields().ToDictionary(f => f.Name, StringComparer.Ordinal);
            var count = 0;

            foreach (var element in doc.Root?.Elements("s") ?? [])
            {
                var name = element.Attribute("name")?.Value;
                if (name is not null && map.TryGetValue(name, out var field))
                {
                    field.SetValue(null, Unescape(element.Value));
                    count++;
                }
            }

            LocalizationLog.App($"Loaded {count} localized string(s) from {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            LocalizationLog.Error($"Reading {Path.GetFileName(path)} failed", ex);
        }
    }

    /// <summary>Captures the currently applied strings so a failed UI-language change can be rolled back without file access.</summary>
    /// <returns>Current UI strings indexed by their resource field names.</returns>
    internal static Dictionary<string,string> CaptureValues() => StringFields().ToDictionary(field => field.Name,field => (string?)field.GetValue(null) ?? "",StringComparer.Ordinal);

    /// <summary>Restores the prior complete UI-language state after a rejected apply operation.</summary>
    /// <param name="language">The resolved UI culture code or source language selected by the operation.</param>
    /// <param name="values">The complete previously applied UI-string snapshot to restore.</param>
    internal static void RestoreValues(string language,IReadOnlyDictionary<string,string> values)
    {
        foreach(var field in StringFields()) if(values.TryGetValue(field.Name,out var value)) field.SetValue(null,value);
        Current = language;
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture; CultureInfo.DefaultThreadCurrentUICulture = culture;
        try { LanguageChanged?.Invoke(); } catch(Exception error) { LocalizationLog.Error("Language rollback notification failed",error); }
    }
    /// <summary>Repairs one shipped language only after preserving its prior user-editable contents.</summary>
    /// <param name="language">One of the seven packaged UI-language codes.</param>
    internal static void RestoreShippedFile(string language)
    {
        string path = PathFor(language);
        if (File.Exists(path))
        {
            string backup = path + "." + Guid.NewGuid().ToString("N") + ".previous";
            File.Copy(path, backup);
            LocalizationLog.App($"Invalid UI language preserved; source={path}; backup={backup}");
        }
        ExtractResource(language, AppPaths.PayloadVersion);
    }
    /// <summary>Extracts a shipped UI language; its stamp follows the actual executable version.</summary>
    /// <param name="language">The resolved UI culture code or source language selected by the operation.</param>
    /// <param name="version">The executable version written into the extracted resource.</param>
    private static void ExtractResource(string language, string version)
    {
        string resource = ResourcePrefix + language + ".xml";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException("Missing embedded UI language.", resource);
        var document = XDocument.Load(stream);
        document.Root?.Element("Version")?.Remove();
        document.Root?.AddFirst(new XElement("Version", version));
        PortableFile.WriteAllText(PathFor(language), document.ToString());
        LocalizationLog.App($"Wrote lang.{language}.xml");
    }
    // Multi-line strings are stored single-line with a literal \n so the XML stays clean/indentable.

    /// <summary>Restores persisted literal newline escapes to UI text.</summary>
    /// <param name="s">Persisted text whose literal newline escapes are restored.</param>
    /// <returns>UI text with persisted newline escape sequences restored.</returns>
    private static string Unescape(string s) => s.Replace("\\n", "\n");
}

