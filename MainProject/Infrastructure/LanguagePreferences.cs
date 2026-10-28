namespace ResilientTranslator.MainProject.Infrastructure;
/// <summary>Stores one immutable language-preference snapshot for the serialized settings writer.</summary>
internal sealed record LanguagePreferences(string Source,string Target,string LastSource,string LastTarget);
