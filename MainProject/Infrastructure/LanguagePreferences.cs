namespace ResilientTranslator.MainProject.Infrastructure;
/// <summary>Stores one immutable language-preference snapshot for the serialized settings writer.</summary>
/// <param name="Source">The source provider, language or snapshot identified by this operation.</param>
/// <param name="Target">The resolved destination language or verified payload executable.</param>
/// <param name="LastSource">The last concrete source selection, separate from automatic detection.</param>
/// <param name="LastTarget">The last concrete target selection, separate from automatic detection.</param>
internal sealed record LanguagePreferences(string Source,string Target,string LastSource,string LastTarget);
