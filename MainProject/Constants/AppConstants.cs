using ResilientTranslator.MainProject.Infrastructure;
namespace ResilientTranslator.MainProject.Constants;

/// <summary>Owns application identity and the portable settings location shared by all layers.</summary>
public static class AppConstants
{
    public const bool TestBuild = false;
    public const bool PublishMode = false;
    public const string AppName = "ResilientTranslator";
    public const string AppNameHumanReadable = "Resilient Translator";
    public const string AppTitle = AppNameHumanReadable;
    public const string CommentPrefix = "#";
    public const string KeyValueSeparator = "=";
    public static readonly string ThisExePath = Environment.ProcessPath!;
    public static readonly string ThisExeFolder = Path.GetDirectoryName(ThisExePath)!;
    public static readonly string ConfigFileName = Path.Combine(AppPaths.UserData,"settings.txt");
}
