namespace ResilientTranslator.MainProject.Infrastructure;
/// <summary>Captures the immutable text and language selections owned by one translation request.</summary>
internal sealed record TranslationInput(string Text,string SourceLanguage,string TargetLanguage,bool ForceNetwork=false);
