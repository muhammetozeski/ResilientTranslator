using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ResilientTranslator.MainProject.Infrastructure.Localization;
namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Captures one online recovery burst and the pause before its next repetition.</summary>
/// <param name="Id">The stable key of a built-in or user-created preset.</param>
/// <param name="Name">The user-provided label of a custom preset.</param>
/// <param name="AttemptSeconds">The duration limit of one provider attempt.</param>
/// <param name="ConcurrentRetries">Additional concurrent-provider attempts within a burst.</param>
/// <param name="ConcurrentSeconds">The concurrent-provider phase budget.</param>
/// <param name="SequentialRetries">Additional sequential-provider attempts within a burst.</param>
/// <param name="SequentialSeconds">The sequential-provider phase budget.</param>
/// <param name="TotalSeconds">The total online recovery burst budget.</param>
/// <param name="PauseMilliseconds">The delay before another burst while the request remains current.</param>
public sealed record TranslationProfile(string Id, string Name, double AttemptSeconds, int ConcurrentRetries,
    double ConcurrentSeconds, int SequentialRetries, double SequentialSeconds, double TotalSeconds, int PauseMilliseconds)
{
    [JsonIgnore]
    public string Label => Id is "responsive" or "balanced" or "quality"
        ? FeatureText.Get("Profile" + char.ToUpperInvariant(Id[0]) + Id[1..]) : Name;
}

/// <summary>Validates, retains and persists online recovery presets through the existing setting registry.</summary>
public static class TranslationProfiles
{
    const bool IsLogEnabled = true;
    static readonly SemaphoreSlim EditGate = new(1, 1);
    static readonly object CacheGate = new();
    static readonly IReadOnlyList<TranslationProfile> Presets = Array.AsReadOnly<TranslationProfile>([
        new("responsive", "", 2, 0, 2, 0, 2, 4, 1000),
        new("balanced", "", 4, 1, 8.5, 0, 4.5, 13, 8000),
        new("quality", "", 8, 2, 25, 1, 16, 41, 12000)]);
    static readonly string[] RequiredMembers = [nameof(TranslationProfile.Id), nameof(TranslationProfile.Name),
        nameof(TranslationProfile.AttemptSeconds), nameof(TranslationProfile.ConcurrentRetries), nameof(TranslationProfile.ConcurrentSeconds),
        nameof(TranslationProfile.SequentialRetries), nameof(TranslationProfile.SequentialSeconds), nameof(TranslationProfile.TotalSeconds),
        nameof(TranslationProfile.PauseMilliseconds)];
    static ProfileCollection? Cached;
    static string? LastRoutingFailure;

    /// <summary>Retains valid custom records without changing their original stored JSON.</summary>
    sealed record ProfileCollection(string Raw, TranslationProfile[] Valid, bool HasInvalidData);

    public static IReadOnlyList<TranslationProfile> BuiltIn => Presets;
    public static IReadOnlyList<TranslationProfile> All => [.. Presets, .. ReadCustom().Valid];

    /// <summary>Creates a valid routing snapshot without discarding unrelated or malformed saved user data.</summary>
    public static AutoRoutingOptions Routing
    {
        get
        {
            string first = TranslationLanguageRules.IsConcrete(Settings.AutoFirstLanguage.Value) ? Settings.AutoFirstLanguage.Value : "en";
            string second = TranslationLanguageRules.IsConcrete(Settings.AutoSecondLanguage.Value) && Settings.AutoSecondLanguage.Value != first
                ? Settings.AutoSecondLanguage.Value : first == "tr" ? "en" : "tr";
            var mode = Enum.TryParse<AutoLanguageMode>(Settings.AutoLanguageMode.Value, out var selected) && Enum.IsDefined(selected)
                ? selected : AutoLanguageMode.Simple;
            LanguageRuleSet rules;
            try { rules = LanguageRuleSet.Parse(Settings.AutoLanguageRules.Value); }
            catch (Exception error) when (error is JsonException or ArgumentException)
            {
                if (LastRoutingFailure != Settings.AutoLanguageRules.Value)
                {
                    LastRoutingFailure = Settings.AutoLanguageRules.Value;
                    ExceptionBoundary.LogSafely($"Invalid routing rules retained; using the simple pair until corrected; error={error}", IsLogEnabled);
                }
                rules = LanguageRuleSet.Empty;
            }
            return new(Settings.TwoWayAutomatic.Value, mode, first, second, rules);
        }
    }

    /// <summary>Migrates only untouched legacy recovery defaults and repairs the active label without replacing user tuning.</summary>
    public static void Initialize()
    {
        if (Settings.RecoveryProfileVersion.Value == 0)
        {
            bool untouched = Settings.AttemptTimeout.Value == 12 && Settings.ConcurrentRetries.Value == 2
                && Settings.ConcurrentBudget.Value == 37 && Settings.SequentialRetries.Value == 1 && Settings.SequentialBudget.Value == 25
                && Settings.ConcurrentRetryDelay.Value == 200 && Settings.SequentialRetryDelay.Value == 300
                && Settings.OnlineTotalBudget.IsDefault && Settings.OnlineRetryPause.IsDefault
                && Settings.ActiveRecoveryProfile.IsDefault && (Settings.RecoveryProfiles.Value ?? "").Trim() == "[]";
            if (untouched) ApplyValues(Presets[1]);
            Settings.RecoveryProfileVersion.Value = 1;
        }
        var active = All.FirstOrDefault(profile => profile.Id == Settings.ActiveRecoveryProfile.Value);
        if (active is null || !SameValues(active, Capture("", ""))) Settings.ActiveRecoveryProfile.Value = "custom";
        _ = Routing;
    }

    /// <summary>Applies a validated preset and reports failure if its new values could not be durably saved.</summary>
    /// <param name="id">The persisted key selected by the profile picker.</param>
    /// <returns>Completion of the accepted preset's durable save.</returns>
    public static async Task ApplyAsync(string id)
    {
        await EditGate.WaitAsync();
        try
        {
            var profile = All.SingleOrDefault(item => item.Id == id) ?? throw new ArgumentException("The selected recovery profile is unavailable.", nameof(id));
            ApplyValues(profile);
            await CommitAsync("apply", profile.Id);
        }
        finally { EditGate.Release(); }
    }

    /// <summary>Captures current validated recovery values as a named custom preset, preserving invalid source data first.</summary>
    /// <param name="name">The nonempty label entered by the user.</param>
    /// <returns>Completion of the new profile's durable save.</returns>
    public static async Task AddAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await EditGate.WaitAsync();
        try
        {
            var profile = Capture(Guid.NewGuid().ToString("N"), name.Trim());
            if (!IsValid(profile)) throw new ArgumentException("Current recovery settings are outside their registered limits.");
            var current = ReadCustom();
            await PreserveInvalidAsync(current);
            Settings.RecoveryProfiles.Value = JsonSerializer.Serialize(current.Valid.Append(profile));
            Settings.ActiveRecoveryProfile.Value = profile.Id;
            await CommitAsync("add", profile.Id);
        }
        finally { EditGate.Release(); }
    }

    /// <summary>Deletes only the selected custom preset while retaining the currently applied timing values.</summary>
    /// <param name="id">The stable key of the custom preset to remove.</param>
    /// <returns>Completion of the updated profile collection's durable save.</returns>
    public static async Task DeleteAsync(string id)
    {
        if (Presets.Any(item => item.Id == id)) throw new InvalidOperationException("Built-in profiles cannot be deleted.");
        await EditGate.WaitAsync();
        try
        {
            var current = ReadCustom();
            if (!current.Valid.Any(item => item.Id == id)) throw new ArgumentException("The custom recovery profile is unavailable.", nameof(id));
            await PreserveInvalidAsync(current);
            Settings.RecoveryProfiles.Value = JsonSerializer.Serialize(current.Valid.Where(item => item.Id != id));
            if (Settings.ActiveRecoveryProfile.Value == id) Settings.ActiveRecoveryProfile.Value = "custom";
            await CommitAsync("delete", id);
        }
        finally { EditGate.Release(); }
    }

    /// <summary>Returns valid individual records and records a malformed source once, without rewriting it.</summary>
    /// <returns>The validated custom records and their original complete JSON text.</returns>
    static ProfileCollection ReadCustom()
    {
        lock (CacheGate)
        {
            string raw = Settings.RecoveryProfiles.Value ?? "";
            if (Cached is { } cached && cached.Raw == raw) return cached;
            var valid = new List<TranslationProfile>();
            var identifiers = Presets.Select(profile => profile.Id).Append("custom").ToHashSet(StringComparer.Ordinal);
            bool invalid = false;
            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Recovery profiles must be an array.");
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    try
                    {
                        if (element.ValueKind != JsonValueKind.Object || RequiredMembers.Any(member => !element.TryGetProperty(member, out _)))
                            throw new JsonException("A recovery profile is missing required fields.");
                        var profile = element.Deserialize<TranslationProfile>();
                        if (profile is null || !IsValid(profile) || !identifiers.Add(profile.Id))
                            throw new JsonException("A recovery profile has invalid values or a duplicate identifier.");
                        valid.Add(profile);
                    }
                    catch (Exception error) when (error is JsonException or ArgumentException)
                    {
                        invalid = true;
                        ExceptionBoundary.LogSafely($"Invalid custom recovery profile omitted from picker; original JSON retained; error={error}", IsLogEnabled);
                    }
                }
            }
            catch (JsonException error)
            {
                invalid = true;
                ExceptionBoundary.LogSafely($"Invalid recovery profile collection retained; built-in presets remain available; error={error}", IsLogEnabled);
            }
            return Cached = new(raw, valid.ToArray(), invalid);
        }
    }

    /// <summary>Checks every profile value against the same metadata used by the advanced settings editor.</summary>
    /// <param name="profile">The deserialized or newly captured custom preset.</param>
    /// <returns>Whether all fields form a usable custom preset.</returns>
    static bool IsValid(TranslationProfile profile) => !string.IsNullOrWhiteSpace(profile.Id) && !string.IsNullOrWhiteSpace(profile.Name)
        && InRange(Settings.AttemptTimeout, profile.AttemptSeconds) && InRange(Settings.ConcurrentRetries, profile.ConcurrentRetries)
        && InRange(Settings.ConcurrentBudget, profile.ConcurrentSeconds) && InRange(Settings.SequentialRetries, profile.SequentialRetries)
        && InRange(Settings.SequentialBudget, profile.SequentialSeconds) && InRange(Settings.OnlineTotalBudget, profile.TotalSeconds)
        && InRange(Settings.OnlineRetryPause, profile.PauseMilliseconds);

    /// <summary>Reads registered bounds through the setting object's identity rather than duplicating numeric limits.</summary>
    /// <param name="setting">The actual registered advanced preference.</param>
    /// <param name="value">The proposed profile value.</param>
    /// <returns>Whether the value is finite and inside the preference's inclusive interval.</returns>
    static bool InRange(ISetting setting, double value)
    {
        var metadata = Preferences.Entries.Single(entry => entry.Setting == setting).Editor;
        return double.IsFinite(value) && value >= metadata.Minimum && value <= metadata.Maximum;
    }

    /// <summary>Retains exact invalid JSON off the UI thread before an explicit edit replaces that collection.</summary>
    /// <param name="collection">The validated collection and its original stored representation.</param>
    /// <returns>Completion of a replay-safe backup, or immediate completion when all records are valid.</returns>
    static async Task PreserveInvalidAsync(ProfileCollection collection)
    {
        if (!collection.HasInvalidData) return;
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(collection.Raw)));
        string path = Path.Combine(AppPaths.UserData, "Backups", $"recovery-profiles.{digest}.invalid.json");
        await Resilience.ExecuteResilientlyAsync(token => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(path)) PortableFile.WriteAllText(path, collection.Raw);
            ExceptionBoundary.LogSafely($"Invalid recovery profile JSON preserved; path={path}", IsLogEnabled);
            return true;
        }, token), static error => error is IOException or UnauthorizedAccessException, operationName: "PreserveInvalidRecoveryProfiles");
    }

    /// <summary>Notifies RAM changes, then requires a successful durable settings save before reporting success.</summary>
    /// <param name="action">The profile action used in diagnostic output.</param>
    /// <param name="id">The affected preset's persisted key.</param>
    /// <returns>Completion of durable persistence; a failed writer produces an exception for the existing UI boundary.</returns>
    static async Task CommitAsync(string action, string id)
    {
        Preferences.NotifyChanged();
        if (!await Preferences.SaveAsync()) throw new IOException("Recovery profile changes could not be durably saved.");
        ExceptionBoundary.LogSafely($"Recovery profile committed; action={action}; profile={id}", IsLogEnabled);
    }

    /// <summary>Captures current timing values without serializing presentation labels or unrelated preferences.</summary>
    /// <param name="id">The new or comparison-only persisted key.</param>
    /// <param name="name">The new or comparison-only display label.</param>
    /// <returns>An immutable recovery snapshot.</returns>
    static TranslationProfile Capture(string id, string name) => new(id, name, Settings.AttemptTimeout.Value,
        Settings.ConcurrentRetries.Value, Settings.ConcurrentBudget.Value, Settings.SequentialRetries.Value, Settings.SequentialBudget.Value,
        Settings.OnlineTotalBudget.Value, Settings.OnlineRetryPause.Value);

    /// <summary>Compares timings independently of a profile's name and persisted key.</summary>
    /// <param name="first">The stored preset.</param>
    /// <param name="second">The active settings snapshot.</param>
    /// <returns>Whether the two snapshots have identical recovery behavior.</returns>
    static bool SameValues(TranslationProfile first, TranslationProfile second) => first with { Id = "", Name = "" } == second with { Id = "", Name = "" };

    /// <summary>Applies a validated built-in or custom preset without changing any other user preference.</summary>
    /// <param name="value">The complete preset accepted by the picker or migration.</param>
    static void ApplyValues(TranslationProfile value)
    {
        Settings.AttemptTimeout.Value = value.AttemptSeconds;
        Settings.ConcurrentRetries.Value = value.ConcurrentRetries; Settings.ConcurrentBudget.Value = value.ConcurrentSeconds;
        Settings.SequentialRetries.Value = value.SequentialRetries; Settings.SequentialBudget.Value = value.SequentialSeconds;
        Settings.OnlineTotalBudget.Value = value.TotalSeconds;
        Settings.OnlineRetryPause.Value = value.PauseMilliseconds; Settings.ActiveRecoveryProfile.Value = value.Id;
    }
}
