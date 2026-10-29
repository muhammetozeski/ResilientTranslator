namespace ResilientTranslator.MainProject.Infrastructure;
/// <summary>Identifies the original engine's execution strategy for one recoverable call.</summary>
public enum TranslationMode { Concurrent, Sequential }
/// <summary>Contains one immutable engine call; diagnostic attempt numbers never determine ownership.</summary>
/// <param name="Text">The complete immutable text accepted by this operation.</param>
/// <param name="SourceLanguage">The source ISO language code captured with this text.</param>
/// <param name="TargetLanguage">The destination ISO language code captured with this text.</param>
/// <param name="Mode">Whether the engine providers execute concurrently or sequentially.</param>
/// <param name="Number">The diagnostic attempt label; it does not determine publication ownership.</param>
public sealed record TranslationAttempt(string Text,string SourceLanguage,string TargetLanguage,TranslationMode Mode,int Number);
/// <summary>Contains a confirmed complete translation and the original provider's identity.</summary>
/// <param name="Text">The complete immutable text accepted by this operation.</param>
/// <param name="Function">The engine operation label accompanying confirmed output.</param>
/// <param name="Service">The successful translation-provider label.</param>
public sealed record ConfirmedTranslation(string Text,string Function,string Service);
