namespace ResilientTranslator.MainProject.UI.Elements.Core;

/// <summary>Shares the actual selector names between the native button and its CSS companion.</summary>
internal static class CoreButtonClasses
{
    public const string Base="core-btn", Standard="core-btn--standard", Compact="core-btn--compact", Bare="core-btn--bare", Full="core-btn--full";
    public static string Appearance(ControlAppearance appearance)=>appearance switch
    {
        ControlAppearance.Standard=>Standard,ControlAppearance.Compact=>Compact,ControlAppearance.Bare=>Bare,
        _=>throw new ArgumentOutOfRangeException(nameof(appearance))
    };
}
