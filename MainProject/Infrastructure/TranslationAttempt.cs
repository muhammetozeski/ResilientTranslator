namespace ResilientTranslator.MainProject.Infrastructure;
/// <summary>Identifies the original engine's execution strategy for one recoverable call.</summary>
public enum TranslationMode { Concurrent, Sequential }
/// <summary>Contains one immutable engine call; diagnostic attempt numbers never determine ownership.</summary>
public sealed record TranslationAttempt(string Text,string SourceLanguage,string TargetLanguage,TranslationMode Mode,int Number);
/// <summary>Contains a confirmed complete translation and the original provider's identity.</summary>
public sealed record ConfirmedTranslation(string Text,string Function,string Service);
