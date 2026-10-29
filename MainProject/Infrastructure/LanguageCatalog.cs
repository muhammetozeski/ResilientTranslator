using ResilientTranslator.MainProject.Infrastructure.Localization;
namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Provides the same language options to the live selectors and startup defaults.</summary>
internal static class LanguageCatalog
{
    /// <summary>Associates an ISO language code with its native selector label.</summary>
    /// <param name="Code">The requested UI culture code or native hook status supplied by the caller.</param>
    /// <param name="Name">The native human-readable language or diagnostic label.</param>
    internal sealed record LanguageChoice(string Code, string Name);
    public static LanguageChoice[] All => [new("auto", Strings.Automatic), new("en", "English"), new("tr", "Türkçe"), new("de", "Deutsch"), new("fr", "Français"), new("es", "Español"), new("it", "Italiano"), new("ru", "Русский")];
}
