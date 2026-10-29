namespace ResilientTranslator.MainProject.Infrastructure.Localization;

/// <summary>
/// Routes the localization engine's original logging calls through the shared global Log method.
/// The engine keeps its existing recovery and fallback behavior.
/// </summary>
internal static class LocalizationLog
{
    private const bool IsLogEnabled = true;

    /// <summary>Routes localization decisions into the existing application logger.</summary>
    /// <param name="message">The complete diagnostic text or native Windows message identifier.</param>
    internal static void App(string message)
    {
        if (IsLogEnabled && LocManager.IsLogEnabled)
        {
            Log(message, Run: IsLogEnabled && LocManager.IsLogEnabled);
        }
    }

    /// <summary>Records localization failures without introducing a second logging sink.</summary>
    /// <param name="message">The complete diagnostic text or native Windows message identifier.</param>
    /// <param name="exception">The original exception that must remain available in the final diagnostic.</param>
    internal static void Error(string message, Exception exception)
    {
        if (IsLogEnabled && LocManager.IsLogEnabled)
        {
            Log($"{message}{Environment.NewLine}{exception}", Run: IsLogEnabled && LocManager.IsLogEnabled);
        }
    }
}


