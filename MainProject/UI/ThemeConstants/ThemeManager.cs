namespace ResilientTranslator.MainProject.Constants.ThemeConstants
{
    /// <summary>
    /// Single source of truth for the active <see cref="AppTheme"/>. Holds the running palette and
    /// notifies the UI when it swaps so every render reflects the new colors immediately.
    /// </summary>
    public static class ThemeManager
    {
        const bool IsLogEnabled = true;
        public static AppTheme Current { get; private set; } = AppThemes.TranslatorDark;

        /// <summary>
        /// Replace the active palette. Fires <see cref="Events.MainEvents.OnDataChanged"/> which the
        /// root layout subscribes to so the entire component tree re-renders against the new theme.
        /// </summary>
        /// <param name="theme">The immutable palette published through the shared component tree.</param>
        public static void Apply(AppTheme theme)
        {
            ArgumentNullException.ThrowIfNull(theme);
            if (theme == Current) return;
            Current = theme;
            Log($"Active palette changed; name={theme.Name}",Run:IsLogEnabled);
            Events.MainEvents.Trigger("OnThemeChanged");
        }
    }
}
