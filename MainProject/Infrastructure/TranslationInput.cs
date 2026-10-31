namespace ResilientTranslator.MainProject.Infrastructure;
/// <summary>Captures the immutable text and language selections owned by one translation request.</summary>
/// <param name="Text">The complete immutable text accepted by this operation.</param>
/// <param name="SourceLanguage">The source ISO language code captured with this text.</param>
/// <param name="TargetLanguage">The destination ISO language code captured with this text.</param>
/// <param name="ForceNetwork">Skips confirmed-result reuse for this explicit user translation command.</param>
internal sealed record TranslationInput(string Text,string SourceLanguage,string TargetLanguage,bool ForceNetwork=false,
    AutoRoutingOptions? Routing=null,Detection.DetectorKind Detector=Detection.DetectorKind.Lingua);
