using System.Collections.Immutable;
using System.Text.Json;

namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Chooses either the configured language pair or the user's ordered language conditions.</summary>
public enum AutoLanguageMode { Simple, Advanced }

/// <summary>Captures both editor selectors and their independent last concrete choices.</summary>
/// <param name="Source">The left editor's selected language or automatic selection.</param>
/// <param name="Target">The right editor's selected language or automatic selection.</param>
/// <param name="LastSource">The last concrete left-side language.</param>
/// <param name="LastTarget">The last concrete right-side language.</param>
public sealed record LanguageSelection(string Source, string Target, string LastSource, string LastTarget);

/// <summary>Contains the concrete source and destination of a single translation.</summary>
/// <param name="Source">The detected or explicitly chosen source language.</param>
/// <param name="Target">The explicit or automatically routed destination language.</param>
public sealed record LanguageDirection(string Source, string Target);

/// <summary>Matches any one of its source languages to one concrete destination.</summary>
public sealed class AutoLanguageRule
{
    /// <summary>Builds one immutable routing condition, rejecting unsupported and automatic language values.</summary>
    /// <param name="sources">One or more alternative source languages joined by OR.</param>
    /// <param name="target">The concrete language produced when this rule matches.</param>
    public AutoLanguageRule(ImmutableArray<string> sources, string target)
    {
        if (sources.IsDefaultOrEmpty || sources.Any(source => !TranslationLanguageRules.IsConcrete(source)))
            throw new ArgumentException("A routing condition requires concrete supported source languages.", nameof(sources));
        if (!TranslationLanguageRules.IsConcrete(target))
            throw new ArgumentException("A routing condition requires a concrete supported target language.", nameof(target));
        Sources = sources.Distinct(StringComparer.Ordinal).ToImmutableArray();
        Target = target;
    }

    /// <summary>Gets the source languages for this OR condition.</summary>
    public ImmutableArray<string> Sources { get; }
    /// <summary>Gets the translation destination of a matching condition.</summary>
    public string Target { get; }
}

/// <summary>Preserves the order of user conditions; the first matching condition wins.</summary>
/// <param name="Rules">The immutable sequence of routing conditions.</param>
public sealed record LanguageRuleSet(ImmutableArray<AutoLanguageRule> Rules)
{
    public static readonly LanguageRuleSet Empty = new([]);

    /// <summary>Reads the persisted array while preserving all language and condition validation.</summary>
    /// <param name="json">The persisted JSON array; empty input means no custom conditions.</param>
    /// <returns>The validated ordered conditions.</returns>
    public static LanguageRuleSet Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        var rules = JsonSerializer.Deserialize<AutoLanguageRule[]>(json) ?? throw new JsonException("The rule array cannot be null.");
        if (rules.Any(rule => rule is null)) throw new JsonException("Routing conditions cannot be null.");
        return new(rules.ToImmutableArray());
    }

    /// <summary>Writes the ordered conditions as the same JSON array accepted by <see cref="Parse"/>.</summary>
    /// <returns>A complete immutable rules snapshot for the settings writer.</returns>
    public string Serialize() => JsonSerializer.Serialize(Rules.IsDefault ? [] : Rules);
}

/// <summary>Captures the routing preferences once for an immutable translation request.</summary>
/// <param name="Enabled">Whether both editor selectors may remain automatic.</param>
/// <param name="Mode">The automatic destination-selection mode.</param>
/// <param name="FirstLanguage">The first language of the simple pair and fallback for other detected languages.</param>
/// <param name="SecondLanguage">The second, distinct language of the simple pair.</param>
/// <param name="Rules">The ordered user conditions used by advanced mode.</param>
public sealed record AutoRoutingOptions(bool Enabled, AutoLanguageMode Mode, string FirstLanguage, string SecondLanguage, LanguageRuleSet Rules)
{
    public static readonly AutoRoutingOptions Default = new(false, AutoLanguageMode.Simple, "en", "tr", LanguageRuleSet.Empty);
}

/// <summary>Owns selector switches, automatic destination fallbacks and two-way routing in one ordered rule table.</summary>
public static class TranslationLanguageRules
{
    public const string Automatic = "auto";
    public const string FallbackLanguage = "en";

    /// <summary>Distinguishes the lifecycle event that selects applicable built-in language rules.</summary>
    enum Stage { Initialization, Selection, Edit }
    /// <summary>Retains the previous selector state while each rule produces the next immutable state.</summary>
    sealed record Context(Stage Stage, LanguageSelection Previous, LanguageSelection Selection, AutoRoutingOptions Options,
        bool FromInput = true, bool HasText = false, bool InputHasText = false, string? LastDetectedInput = null)
    {
        public bool BothAutomatic => Selection.Source == Automatic && Selection.Target == Automatic;
        public bool SourceChanged => Selection.Source != Previous.Source;
        public bool PreserveAutomatic => BothAutomatic && Options.Enabled;
    }
    /// <summary>Associates one matching predicate with its selector-state transformation.</summary>
    sealed record SelectionRule(Func<Context, bool> Matches, Func<Context, LanguageSelection> Apply);

    // New selector/edit rules are added here. All entry points execute this same ordered policy.
    static readonly SelectionRule[] BuiltInRules =
    [
        new(static c => c.Stage == Stage.Initialization && c.BothAutomatic && !c.Options.Enabled,
            static c => c.Selection with { Target = FallbackLanguage }),
        new(static c => c.Stage == Stage.Selection && c.BothAutomatic && !c.Options.Enabled,
            static c => c.SourceChanged
                ? c.Selection with { Target = c.Previous.LastTarget }
                : c.Selection with { Source = c.Previous.LastSource }),
        new(static c => c.Stage == Stage.Selection && c.Selection.Source == c.Selection.Target && IsConcrete(c.Selection.Source),
            static c => c.SourceChanged
                ? c.Selection with { Target = c.Previous.Source }
                : c.Selection with { Source = c.Previous.Target }),
        new(static c => c.Stage == Stage.Edit && c.HasText && c.FromInput && c.Selection.Target == Automatic && !c.PreserveAutomatic,
            static c => c.Selection with { Target = FallbackLanguage, LastTarget = FallbackLanguage }),
        new(static c => c.Stage == Stage.Edit && c.HasText && !c.FromInput && c.Selection.Source == Automatic && !c.PreserveAutomatic,
            static c => c.Selection with
            {
                Source = c.InputHasText && IsConcrete(c.LastDetectedInput) ? c.LastDetectedInput! : FallbackLanguage,
                LastSource = c.InputHasText && IsConcrete(c.LastDetectedInput) ? c.LastDetectedInput! : FallbackLanguage
            }),
        new(static c => c.Stage == Stage.Selection,
            static c => c.Selection with
            {
                LastSource = IsConcrete(c.Selection.Source) ? c.Selection.Source : c.Selection.LastSource,
                LastTarget = IsConcrete(c.Selection.Target) ? c.Selection.Target : c.Selection.LastTarget
            })
    ];

    /// <summary>Identifies the concrete languages supported by both selectors and routing conditions.</summary>
    /// <param name="language">The canonical provider language code.</param>
    /// <returns>Whether the code names one of the seven concrete application languages.</returns>
    public static bool IsConcrete(string? language) => language is "en" or "tr" or "de" or "fr" or "es" or "it" or "ru";

    /// <summary>Rejects a routing configuration whose simple pair cannot choose an opposite language.</summary>
    /// <param name="options">The options to validate before accepting a settings edit.</param>
    public static void ValidateOptions(AutoRoutingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Mode) || !IsConcrete(options.FirstLanguage) || !IsConcrete(options.SecondLanguage)
            || options.FirstLanguage == options.SecondLanguage || options.Rules is null || options.Rules.Rules.IsDefault
            || options.Rules.Rules.Any(rule => rule is null))
            throw new ArgumentException("Automatic routing requires two distinct supported languages and a valid rule set.", nameof(options));
    }

    /// <summary>Applies the startup automatic-pair rule while retaining the user's concrete-language history.</summary>
    /// <param name="selection">The initial selector values and their saved history.</param>
    /// <param name="options">The routing preferences restored at startup.</param>
    /// <returns>The normalized initial selector state.</returns>
    public static LanguageSelection NormalizeInitialization(LanguageSelection selection, AutoRoutingOptions options) =>
        ApplyRules(new(Stage.Initialization, selection, selection, options));

    /// <summary>Switches colliding selector choices using the previously selected language and saved concrete history.</summary>
    /// <param name="previous">The complete selector state before the user action.</param>
    /// <param name="source">The requested new left-side selection.</param>
    /// <param name="target">The requested new right-side selection.</param>
    /// <param name="options">Whether simultaneous automatic selections are enabled.</param>
    /// <returns>The selector state after all applicable switch and history rules.</returns>
    public static LanguageSelection NormalizeSelection(LanguageSelection previous, string source, string target, AutoRoutingOptions options) =>
        ApplyRules(new(Stage.Selection, previous, previous with { Source = source, Target = target }, options));

    /// <summary>Applies the existing English and reverse-detection fallbacks when accepting an editor change.</summary>
    /// <param name="selection">The editor selector state before the edit.</param>
    /// <param name="fromInput">Whether the left editor is the source of this translation.</param>
    /// <param name="hasText">Whether the edited text contains any non-whitespace content.</param>
    /// <param name="inputHasText">Whether the left editor contains any non-whitespace content.</param>
    /// <param name="lastDetectedInputCode">The last concrete language detected from the left editor, if known.</param>
    /// <param name="options">The immutable routing preferences for this edit.</param>
    /// <returns>The selector state to persist and display before submitting the request.</returns>
    public static LanguageSelection NormalizeEdit(LanguageSelection selection, bool fromInput, bool hasText, bool inputHasText,
        string? lastDetectedInputCode, AutoRoutingOptions options) =>
        ApplyRules(new(Stage.Edit, selection, selection, options, fromInput, hasText, inputHasText, lastDetectedInputCode));

    /// <summary>Resolves a detected source into the final concrete translation direction without mutating editor selectors.</summary>
    /// <param name="source">The request's source selector, including automatic.</param>
    /// <param name="target">The request's destination selector, including automatic.</param>
    /// <param name="detectedCode">The result of local language detection, if available.</param>
    /// <param name="options">The preferences captured for this request.</param>
    /// <returns>The concrete source and destination to submit to a translation engine.</returns>
    public static LanguageDirection ResolveRequest(string source, string target, string? detectedCode, AutoRoutingOptions options)
    {
        bool routeDestination = options.Enabled && source == Automatic && target == Automatic;
        return new(source == Automatic ? IsConcrete(detectedCode) ? detectedCode! : FallbackLanguage : source,
            routeDestination ? ResolveAutomaticTarget(detectedCode, options) : target == Automatic ? FallbackLanguage : target);
    }

    /// <summary>Uses the first matching visual condition, then the configured simple language pair.</summary>
    /// <param name="detectedCode">The canonical language detected from the editor being translated.</param>
    /// <param name="options">The immutable mode, pair and ordered condition list.</param>
    /// <returns>The configured opposite language, or the first simple language when no condition or pair matches.</returns>
    public static string ResolveAutomaticTarget(string? detectedCode, AutoRoutingOptions options)
    {
        ValidateOptions(options);
        if (options.Mode == AutoLanguageMode.Advanced && detectedCode is not null)
            foreach (var rule in options.Rules.Rules)
                if (rule.Sources.Contains(detectedCode, StringComparer.Ordinal)) return rule.Target;
        return detectedCode == options.FirstLanguage ? options.SecondLanguage : options.FirstLanguage;
    }

    /// <summary>Runs each matching built-in rule over one immutable action context.</summary>
    /// <param name="context">The selector action and routing preferences captured by the caller.</param>
    /// <returns>The final selector state after all applicable rules.</returns>
    static LanguageSelection ApplyRules(Context context)
    {
        foreach (var rule in BuiltInRules)
            if (rule.Matches(context)) context = context with { Selection = rule.Apply(context) };
        return context.Selection;
    }
}
