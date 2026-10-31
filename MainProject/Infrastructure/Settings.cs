using ResilientTranslator.MainProject.Infrastructure.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Contains serialization metadata and aliases the application&apos;s portable settings location.</summary>
internal static class SettingsManagerSettings
{
    public static string ConfigFileName => AppConstants.ConfigFileName;
    public const string CommentPrefix = "#";
    public const string KeyValueSeparator = "=";
    public const string AppTitle = AppConstants.AppTitle;
    public static readonly string ThisExeFolder = AppContext.BaseDirectory;
    public static bool IsTurkish;
    public const string DescriptionFormatEnglish = "{0} expects {1}. Default: {2}.";
    public const string DescriptionFormatTurkish = "{0} için {1} girin. Varsayılan değer: {2}.";
    public const string ExampleFormatEnglish = " Example: {0}.";
    public const string ExampleFormatTurkish = " Örnek: {0}.";
    public const string OptionalDescriptionEnglish = " You may leave it blank.";
    public const string OptionalDescriptionTurkish = " Boş bırakabilirsiniz.";
}

// Declare settings here. Field names become file keys automatically.
/// <summary>Declares the application&apos;s persistent typed values and reflected UI metadata.</summary>
internal static class Settings
{
    [Preference(nameof(Strings.ApplicationLanguage))]
    public static readonly Setting<string> Language = new(LocManager.SystemLanguage);
    public static readonly Setting<string> SourceLanguage = new("en");
    public static readonly Setting<string> TargetLanguage = new("tr");
    public static readonly Setting<string> LastConcreteSourceLanguage = new("en");
    public static readonly Setting<string> LastConcreteTargetLanguage = new("tr");
    [Preference(nameof(Strings.SettingEnableLogging), true)]
    public static readonly Setting<bool> EnableLogging = new(true);
    [Preference(nameof(Strings.SettingDefaultSourceLanguage))]
    public static readonly Setting<string> DefaultSourceLanguage = new("");
    [Preference(nameof(Strings.SettingDefaultTargetLanguage))]
    public static readonly Setting<string> DefaultTargetLanguage = new("");
    [Preference(nameof(Strings.SettingStatusSpeed), minimum: 5, maximum: 150)]
    public static readonly Setting<double> StatusSpeed = new(70);
    [Preference(nameof(Strings.SettingEditorFont))]
    public static readonly Setting<string> EditorFont = new(AppMeasures.Font.DefaultFamily);
    [Preference(nameof(Strings.SettingEditorFontSize), minimum: 1, maximum: 300)]
    public static readonly Setting<double> EditorFontSize = new(AppMeasures.Font.Normal);
    [Preference(nameof(Strings.SettingEditorColor))]
    public static readonly Setting<string> EditorColor = new("#ffffff");
    [Preference(nameof(Strings.SettingTypingDelay), true, 0, 10000)]
    public static readonly Setting<int> TypingDelay = new(1000);
    [Preference(nameof(Strings.SettingConcurrentRetries), true, 0, 10)]
    public static readonly Setting<int> ConcurrentRetries = new(2);
    [Preference(nameof(Strings.SettingSequentialRetries), true, 0, 10)]
    public static readonly Setting<int> SequentialRetries = new(1);
    [Preference(nameof(Strings.SettingAttemptTimeout), true, 1, 120)]
    public static readonly Setting<double> AttemptTimeout = new(12);
    [Preference(nameof(Strings.SettingConcurrentBudget), true, 1, 600)]
    public static readonly Setting<double> ConcurrentBudget = new(37);
    [Preference(nameof(Strings.SettingSequentialBudget), true, 1, 600)]
    public static readonly Setting<double> SequentialBudget = new(25);
    [Preference(nameof(Strings.SettingConcurrentRetryDelay), true, 0, 10000)]
    public static readonly Setting<int> ConcurrentRetryDelay = new(200);
    [Preference(nameof(Strings.SettingSequentialRetryDelay), true, 0, 10000)]
    public static readonly Setting<int> SequentialRetryDelay = new(300);
    [Preference(nameof(Strings.SettingChunkByteLimit), true, 100, 1000)]
    public static readonly Setting<int> ChunkByteLimit = new(1000);
    [Preference(nameof(Strings.SettingMaximumCacheEntries), true, 0, 1024)]
    public static readonly Setting<int> MaximumCacheEntries = new(128);
    [Preference(nameof(Strings.SettingMaximumCachedCharacters), true, 0, 100000)]
    public static readonly Setting<int> MaximumCachedCharacters = new(8192);

    [Preference(nameof(Strings.SettingDetectorGateBudget), true, 0, 5000)]
    public static readonly Setting<int> DetectorGateBudget = new(250);
    [Preference(nameof(Strings.SettingClipboardChangeBudget), true, 50, 5000)]
    public static readonly Setting<int> ClipboardChangeBudget = new(450);
    [Preference(nameof(Strings.SettingInputShare), minimum: 10, maximum: 90)]
    public static readonly Setting<double> InputShare = new(50);
    [Preference(nameof(Strings.SettingCenterSnapRange), true, 0, 80)]
    public static readonly Setting<int> CenterSnapRange = new(6);
    public static readonly Setting<int> WindowWidth = new(1000);
    public static readonly Setting<int> WindowHeight = new(560);
    [Preference(nameof(Strings.OnlineEnabled))] public static readonly Setting<bool> OnlineEnabled = new(true);
    [Preference(nameof(Strings.DetectorLabel))] public static readonly Setting<string> DetectorChoice = new("Lingua");
    [Preference(nameof(Strings.TwoWayAutomatic))] public static readonly Setting<bool> TwoWayAutomatic = new(false);
    [Preference(nameof(Strings.AutoLanguageMode))] public static readonly Setting<string> AutoLanguageMode = new("Simple");
    [Preference(nameof(Strings.FirstLanguage))] public static readonly Setting<string> AutoFirstLanguage = new("en");
    [Preference(nameof(Strings.SecondLanguage))] public static readonly Setting<string> AutoSecondLanguage = new("tr");
    [Preference(nameof(Strings.RoutingRules))] public static readonly Setting<string> AutoLanguageRules = new("[]");
    [Preference(nameof(Strings.UseBergamot),true)] public static readonly Setting<bool> UseBergamot = new(true);
    [Preference(nameof(Strings.UseToz),true)] public static readonly Setting<bool> UseToz = new(true);
    [Preference(nameof(Strings.BergamotWorkers),true,1,8)] public static readonly Setting<int> BergamotWorkers = new(2);
    [Preference(nameof(Strings.TozWorkers),true,1,4)] public static readonly Setting<int> TozWorkers = new(1);
    [Preference(nameof(Strings.TozThreads),true,1,8)] public static readonly Setting<int> TozThreads = new(1);
    [Preference(nameof(Strings.BergamotBeam),true,1,8)] public static readonly Setting<int> BergamotBeam = new(4);
    [Preference(nameof(Strings.TozBeam),true,1,8)] public static readonly Setting<int> TozBeam = new(4);
    [Preference(nameof(Strings.ModelLoadTimeout),true,1000,180000)] public static readonly Setting<int> ModelLoadTimeout = new(60000);
    [Preference(nameof(Strings.LocalTranslationTimeout),true,1000,120000)] public static readonly Setting<int> LocalTranslationTimeout = new(30000);
    [Preference(nameof(Strings.LocalRetries),true,0,5)] public static readonly Setting<int> LocalRetries = new(1);
    [Preference(nameof(Strings.LocalRetryDelay),true,0,5000)] public static readonly Setting<int> LocalRetryDelay = new(100);
    [Preference(nameof(Strings.OnlineTotalBudget),true,1,120)] public static readonly Setting<double> OnlineTotalBudget = new(13);
    [Preference(nameof(Strings.OnlineRetryPause),true,100,60000)] public static readonly Setting<int> OnlineRetryPause = new(8000);
    [Preference(nameof(Strings.RecoveryProfile))] public static readonly Setting<string> ActiveRecoveryProfile = new("balanced");
    public static readonly Setting<string> RecoveryProfiles = new("[]");
    public static readonly Setting<int> RecoveryProfileVersion = new(0);
    [Preference(nameof(Strings.BergamotCachedDirections),true,1,2)] public static readonly Setting<int> BergamotCachedDirections = new(2);
    [Preference(nameof(Strings.DetectorModelLoadTimeout),true,1000,120000)] public static readonly Setting<int> DetectorModelLoadTimeout = new(15000);
    [Preference(nameof(Strings.DetectorTimeout),true,1,30000)] public static readonly Setting<int> DetectorTimeout = new(3000);
    [Preference(nameof(Strings.DetectorMaximumCharacters),true,10,1000000)] public static readonly Setting<int> DetectorMaximumCharacters = new(20000);
}

/// <summary>Exposes a registered typed setting to generic validation and settings editors.</summary>
internal interface ISetting
{
    string Key { get; }
    string KeyHumanReadable { get; }
    string DisplayName { get; }
    string Description { get; }
    string? ValueExample { get; }
    bool IsOptional { get; }
    Type ValueType { get; }
    object? Value { get; set; }
    object? DefaultValue { get; }
    bool IsDefault { get; }
    /// <summary>Accepts an unencoded editor value only when parsing succeeds, preserving the current value on rejection.</summary>
    /// <param name="text">The unencoded editor value; rejection preserves the current setting.</param>
    /// <returns>True when the typed in-memory setting was updated.</returns>
    bool TrySetFromText(string text);
    /// <summary>Loads an editor-format value; invalid input restores the declared default and reports a format error.</summary>
    /// <param name="value">The encoded or editor-format setting value accepted by this serialization operation.</param>
    void LoadFromStr(string value);
    /// <summary>Restores this preference&apos;s declared default in memory without writing its file.</summary>
    void ResetToDefault();
}

/// <summary>Connects field-name registration to encoded settings serialization.</summary>
internal interface ISettingSetup
{
    string Key { get; }
    /// <summary>Binds this setting to its declaring field name exactly once.</summary>
    /// <param name="key">The declaring field name used as the persistent setting key.</param>
    void InitializeKey(string key);
    /// <summary>Loads an editor-format value; invalid input restores the declared default and reports a format error.</summary>
    /// <param name="value">The encoded or editor-format setting value accepted by this serialization operation.</param>
    void LoadFromStr(string value);
    /// <summary>Parses one persisted representation before changing the stored typed value.</summary>
    /// <param name="value">The encoded value read from the persistent settings file.</param>
    /// <returns>True only when the persisted representation is valid for the setting type.</returns>
    bool TryLoadSerialized(string value);
    /// <summary>Encodes this setting without losing nulls, whitespace or invariant numeric values.</summary>
    /// <returns>The setting&apos;s encoded file representation.</returns>
    string Serialize();
}

/// <summary>Stores one typed preference, its default and its parser/serialization contract.</summary>
/// <typeparam name="T">The typed preference value supported by its parser and serializer.</typeparam>
internal class Setting<T> : ISetting, ISettingSetup
{
    private static readonly Type ParsedType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
    private static readonly TypeConverter Converter = TypeDescriptor.GetConverter(ParsedType);
    private static readonly bool CanReadText = Converter.CanConvertFrom(typeof(string));
    private static readonly bool CanWriteText = Converter.CanConvertTo(typeof(string));
    private static readonly bool AllowsNull = !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;
    private static readonly string TypeName = SettingsManager.SplitPascalCase(ParsedType.Name);
    private static readonly InterfaceMapping? ParsableMap = ParsedType.GetInterfaces().FirstOrDefault(contract =>
        contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IParsable<>)
        && contract.GetGenericArguments()[0] == ParsedType) is { } contract ? ParsedType.GetInterfaceMap(contract) : null;
    private static readonly MethodInfo? ParseMethod = ParsableMap is { } map
        ? map.TargetMethods[Array.FindIndex(map.InterfaceMethods, method => method.Name == "Parse")] : null;
    private readonly string? _displayName;
    private readonly string? _valueExample;
    private readonly string? _description;

    /// <param name="defaultValue">Initial value and fallback for an invalid file value.</param>
    /// <param name="description">Optional explanation; generated from the other metadata when blank.</param>
    /// <param name="displayName">Optional label; the field name is otherwise split into words.</param>
    /// <param name="valueExample">Optional input example; the default value is used when blank.</param>
    /// <param name="isOptional">Whether text input may be blank or null when the type allows it.</param>
    /// <summary>Declares a preference with its initial value and optional editor metadata.</summary>
    public Setting(T defaultValue, string? description = null, string? displayName = null, string? valueExample = null, bool isOptional = true)
    {
        Value = defaultValue;
        DefaultValue = defaultValue;
        _description = description;
        _displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName;
        _valueExample = valueExample;
        IsOptional = isOptional;
    }

    public string Key { get; private set; } = string.Empty;
    public string KeyHumanReadable { get; private set; } = string.Empty;
    public string DisplayName => _displayName ?? KeyHumanReadable;
    public string Description
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_description)) return _description;
            bool turkish = SettingsManagerSettings.IsTurkish;
            string? defaultText = DefaultValue?.ToString();
            string defaultDescription = defaultText == null ? (turkish ? "belirtilmemiş" : "not set")
                : defaultText.Length == 0 ? (turkish ? "boş metin" : "empty text")
                : DefaultValue is string ? $"\"{defaultText}\"" : defaultText;
            string description = string.Format(CultureInfo.CurrentCulture, turkish ? SettingsManagerSettings.DescriptionFormatTurkish : SettingsManagerSettings.DescriptionFormatEnglish, DisplayName, TypeName, defaultDescription);
            if (!string.IsNullOrWhiteSpace(ValueExample) && ValueExample != defaultText) description += string.Format(CultureInfo.CurrentCulture, turkish ? SettingsManagerSettings.ExampleFormatTurkish : SettingsManagerSettings.ExampleFormatEnglish, ValueExample);
            if (IsOptional && AllowsNull) description += turkish ? SettingsManagerSettings.OptionalDescriptionTurkish : SettingsManagerSettings.OptionalDescriptionEnglish;
            return description;
        }
    }
    public string? ValueExample => !string.IsNullOrWhiteSpace(_valueExample) ? _valueExample : DefaultValue?.ToString();
    public bool IsOptional { get; }
    public Type ValueType => typeof(T);
    public T Value;
    public readonly T DefaultValue;
    public bool IsDefault => EqualityComparer<T>.Default.Equals(Value, DefaultValue);

    object? ISetting.Value { get => Value; set => Value = (T)value!; }
    object? ISetting.DefaultValue => DefaultValue;

    /// <summary>Binds this setting to its declaring field name exactly once.</summary>
    /// <param name="key">The declaring field name used as the persistent setting key.</param>
    void ISettingSetup.InitializeKey(string key)
    {
        if (!string.IsNullOrEmpty(Key))
            throw new InvalidOperationException($"Key is already initialized to '{Key}'.");
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        KeyHumanReadable = SettingsManager.SplitPascalCase(key);
    }

    // A rejected UI edit leaves the current value intact; a bad file value uses the default.
    /// <summary>Accepts an unencoded editor value only when parsing succeeds, preserving the current value on rejection.</summary>
    /// <param name="text">The unencoded editor value; rejection preserves the current setting.</param>
    /// <returns>True when the typed in-memory setting was updated.</returns>
    public bool TrySetFromText(string text)
    {
        if (!TryParse(text, encoded: false, out T parsed)) return false;
        Value = parsed;
        return true;
    }

    /// <summary>Loads an editor-format value; invalid input restores the declared default and reports a format error.</summary>
    /// <param name="value">The encoded or editor-format setting value accepted by this serialization operation.</param>
    public void LoadFromStr(string value)
    {
        if (TrySetFromText(value)) return;
        ResetToDefault();
        throw new FormatException($"Invalid {typeof(T).Name} for '{Key}': '{value}'. Using the default.");
    }

    /// <summary>Restores this preference&apos;s declared default in memory without writing its file.</summary>
    public void ResetToDefault() => Value = DefaultValue;

    /// <summary>Parses one persisted representation before changing the stored typed value.</summary>
    /// <param name="text">The encoded value read from the persistent settings file.</param>
    /// <returns>True only when the persisted representation is valid for the setting type.</returns>
    bool ISettingSetup.TryLoadSerialized(string text)
    {
        if (!TryParse(text, encoded: true, out T parsed)) return false;
        Value = parsed;
        return true;
    }

    /// <summary>Converts editor or persisted text through the setting type&apos;s supported conversion contract.</summary>
    /// <param name="text">The setting text to parse without changing its stored value on rejection.</param>
    /// <param name="encoded">Whether the input uses the persisted serialization syntax.</param>
    /// <param name="parsed">Receives the typed result only when conversion succeeds.</param>
    /// <returns>True only when a typed value was parsed successfully.</returns>
    private bool TryParse(string text, bool encoded, out T parsed)
    {
        parsed = default!;
        Type type = ParsedType;
        if (!IsOptional && string.IsNullOrWhiteSpace(text)) return false;

        try
        {
            object? value;
            if (type == typeof(string))
            {
                value = encoded && text == "null" ? null
                    : encoded && text.StartsWith('"') ? JsonSerializer.Deserialize<string>(text) : text;
                if (!IsOptional && string.IsNullOrWhiteSpace((string?)value)) return false;
            }
            else if (text == "null" || string.IsNullOrWhiteSpace(text))
            {
                if (!IsOptional || !AllowsNull) return false;
                value = null;
            }
            else
            {
                if (type == typeof(bool) && Int128.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out Int128 number)) text = (number != 0).ToString();
                if (type.IsEnum) text = Regex.Replace(text, @"\s+", string.Empty);
                value = type == typeof(DateTime) ? DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                    : CanReadText ? Converter.ConvertFromInvariantString(text)
                    : ParseMethod != null ? ParseMethod.Invoke(null, [text, CultureInfo.InvariantCulture])
                    : throw new NotSupportedException($"Type {type.Name} needs a TypeConverter or IParsable implementation.");
            }

            parsed = (T)value!;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Encodes this setting without losing nulls, whitespace or invariant numeric values.</summary>
    /// <returns>The setting&apos;s encoded file representation.</returns>
    string ISettingSetup.Serialize()
    {
        if (Value is null) return "null";
        if (Value is string text)
        {
            // Quote only text that the original key=value format cannot preserve exactly.
            return text == "null" || text.StartsWith('"') || text != text.Trim()
                || text.Contains('\r') || text.Contains('\n')
                ? JsonSerializer.Serialize(text) : text;
        }
        if (Value is DateTime dateTime) return dateTime.ToString("O", CultureInfo.InvariantCulture);
        if (Value is DateTimeOffset offset) return offset.ToString("O", CultureInfo.InvariantCulture);
        return CanWriteText ? Converter.ConvertToInvariantString(Value) ?? string.Empty
            : Value is IFormattable formatted ? formatted.ToString(null, CultureInfo.InvariantCulture) : Value.ToString() ?? string.Empty;
    }

    /// <summary>Reads the current typed preference without creating an independent value store.</summary>
    /// <param name="setting">The registered preference object.</param>
    /// <returns>The currently stored typed value.</returns>
    public static implicit operator T(Setting<T> setting) => setting.Value;
}

/// <summary>Registers typed setting fields and owns the existing key/value serialization contract.</summary>
internal static class SettingsManager
{
    public static string ConfigPath => Path.Combine(SettingsManagerSettings.ThisExeFolder, SettingsManagerSettings.ConfigFileName);
    public static event Action? SettingsSaved;

    private static readonly object FileLock = new();
    private static readonly Dictionary<string, ISettingSetup> iSettingSetups = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ISetting> iSettings = new(StringComparer.Ordinal);

    /// <summary>Registers setting fields and their file keys before the first load or save.</summary>
    static SettingsManager()
    {
        foreach (FieldInfo field in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not ISettingSetup setup) continue;
            setup.InitializeKey(field.Name);
            iSettingSetups.Add(field.Name, setup);
            if (setup is ISetting setting) iSettings.Add(field.Name, setting);
        }
    }

    /// <summary>Returns the registered setting objects used by editors and snapshot serialization.</summary>
    /// <returns>The registered mutable setting objects, not copies with independent identities.</returns>
    public static ISetting[] GetAllSettings() => [.. iSettings.Values];

    /// <summary>Loads settings, creating the file if missing. Invalid values use their defaults and produce a FormatException.</summary>
    /// <param name="filePath">The selected portable settings path; null selects the existing default.</param>
    /// <param name="isTurkish">Selects Turkish metadata descriptions for the existing settings editor.</param>
    public static void LoadSettings(string? filePath = null, bool isTurkish = false)
    {
        lock (FileLock)
        {
            SettingsManagerSettings.IsTurkish = isTurkish;
            string path = Path.GetFullPath(filePath ?? ConfigPath);
            if (!File.Exists(path)) { SaveSettings(path); return; }
            string[] lines = File.ReadAllLines(path);
            foreach (ISetting setting in iSettings.Values) setting.ResetToDefault();

            List<string> errors = [];
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(SettingsManagerSettings.CommentPrefix, StringComparison.Ordinal)) continue;
                int separator = line.IndexOf(SettingsManagerSettings.KeyValueSeparator, StringComparison.Ordinal);
                if (separator < 0)
                {
                    errors.Add($"Invalid settings line: '{line}'.");
                    continue;
                }
                string key = line[..separator].Trim();
                if (!iSettings.TryGetValue(key, out ISetting? setting)) continue;
                string text = line[(separator + SettingsManagerSettings.KeyValueSeparator.Length)..].Trim();
                if (iSettingSetups[key].TryLoadSerialized(text)) continue;
                setting.ResetToDefault();
                errors.Add($"Invalid {setting.ValueType.Name} for '{key}': '{text}'. Using the default.");
            }
            if (errors.Count > 0) throw new FormatException(string.Join(Environment.NewLine, errors));
        }
    }

    /// <summary>Saves settings and raises SettingsSaved after a successful write. Errors propagate to the caller.</summary>
    /// <param name="filePath">The selected portable settings path; null selects the existing default.</param>
    public static void SaveSettings(string? filePath = null) => SaveSnapshot(CaptureSnapshot(), filePath);

    /// <summary>Serializes current values without performing file access; callers hold their state gate.</summary>
    /// <returns>The existing settings engine&apos;s complete immutable serialized snapshot.</returns>
    internal static string CaptureSnapshot()
    {
        var content = new StringBuilder($"{SettingsManagerSettings.CommentPrefix} {SettingsManagerSettings.AppTitle} Configuration{Environment.NewLine}");
        foreach (ISettingSetup setting in iSettingSetups.Values)
            content.AppendLine($"{setting.Key} {SettingsManagerSettings.KeyValueSeparator} {setting.Serialize()}");
        return content.ToString();
    }

    /// <summary>Atomically installs an immutable settings snapshot, preserving the previous file on write failure.</summary>
    /// <param name="content">Already serialized settings.</param>
    /// <param name="filePath">Portable destination or the default config path.</param>
    internal static void SaveSnapshot(string content, string? filePath = null)
    {
        lock (FileLock) PortableFile.WriteAllText(filePath ?? ConfigPath, content);
        SettingsSaved?.Invoke();
    }
    /// <summary>Applies a parsed editor value without replacing an invalid current value.</summary>
    /// <param name="setting">The registered setting object being parsed, validated or edited.</param>
    /// <param name="value">The encoded or editor-format setting value accepted by this serialization operation.</param>
    /// <returns>True only when the editor value was accepted.</returns>
    public static bool Apply(ISetting setting, string value) => setting.TrySetFromText(value);

    /// <summary>Opens an optional console settings editor. Changes are saved immediately.</summary>
    /// <param name="filePath">The selected portable settings path; null selects the existing default.</param>
    public static void OpenSettingsMenu(string? filePath = null)
    {
        ISetting[] allSettings = GetAllSettings();
        bool turkish = SettingsManagerSettings.IsTurkish;
        while (true)
        {
            Console.Clear();
            Console.WriteLine($"{SettingsManagerSettings.AppTitle} - {(turkish ? "Ayarlar" : "Settings")}{Environment.NewLine}");
            for (int i = 0; i < allSettings.Length; i++) Console.WriteLine($" [{i + 1}] {allSettings[i].DisplayName,-25}: {allSettings[i].Value?.ToString() ?? "null"}");
            Console.WriteLine(turkish ? " [0] Geri" : " [0] Back");
            Console.Write("\n> ");

            ConsoleKeyInfo key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Escape || key.KeyChar == '0') { SaveSettings(filePath); return; }
            if (!char.IsDigit(key.KeyChar)) continue;
            string selection = key.KeyChar.ToString();
            if (allSettings.Length > 9) { Console.Write(selection); selection += Console.ReadLine(); }
            if (!int.TryParse(selection, out int number) || number < 1 || number > allSettings.Length) continue;

            ISetting setting = allSettings[number - 1];
            if (setting.Value is bool flag) setting.Value = !flag;
            else
            {
                Console.WriteLine($"\n{setting.Description}");
                Console.Write($"{setting.DisplayName} [{setting.ValueExample}]: ");
                string? input = Console.ReadLine();
                if (input == null) { SaveSettings(filePath); return; }
                if (!setting.TrySetFromText(input))
                {
                    Console.WriteLine(turkish ? "Geçersiz değer. Devam etmek için bir tuşa basın." : "Invalid value. Press a key to continue.");
                    Console.ReadKey(true);
                    continue;
                }
            }
            SaveSettings(filePath);
        }
    }

    /// <summary>Resets in-memory values. Call SaveSettings to persist them.</summary>
    public static void ResetAllToDefaults()
    {
        foreach (ISetting setting in iSettings.Values) setting.ResetToDefault();
    }

    /// <summary>Builds a readable metadata label from a registered PascalCase field or type name.</summary>
    /// <param name="text">The setting text to parse without changing its stored value on rejection.</param>
    /// <returns>The human-readable metadata name.</returns>
    internal static string SplitPascalCase(string text) => Regex.Replace(text, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
}



