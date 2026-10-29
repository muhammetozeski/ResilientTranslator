using Polly;

namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Recovers portable settings through the existing serializer, retaining invalid input and durable pending writes.</summary>
internal static class SettingsPersistence
{
    const bool IsLogEnabled = true;
    const int RetryCount = 2;
    static readonly object Gate = new();

    /// <summary>Loads the newest committed or pending snapshot and repairs invalid values without losing their original file.</summary>
    internal static void Load()
    {
        lock (Gate)
        {
            string primary = AppConstants.ConfigFileName;
            string pending = primary + ".pending";
            string selected = File.Exists(pending) && (!File.Exists(primary) || File.GetLastWriteTimeUtc(pending) > File.GetLastWriteTimeUtc(primary)) ? pending : primary;
            bool repaired = false;
            try { FileRetry().Execute(() => SettingsManager.LoadSettings(selected)); }
            catch (FormatException error)
            {
                Preserve(selected);
                repaired = true;
                ExceptionBoundary.LogSafely($"Invalid setting values retained their parsed defaults; source={selected}; error={error}", Run: IsLogEnabled);
            }
            if (Settings.Language.Value != Localization.LocManager.SystemLanguage &&
                !LanguageCatalog.All.Any(language => language.Code == Settings.Language.Value && language.Code != "auto"))
            {
                if (!repaired && File.Exists(selected)) Preserve(selected);
                Settings.Language.ResetToDefault();
                repaired = true;
                Log("Invalid application language restored to the declared UI default", Run: IsLogEnabled);
            }
            if (selected == pending || repaired) SaveSnapshot(SettingsManager.CaptureSnapshot());
            ExceptionBoundary.LogSafely($"Settings loaded; selected={selected}; repaired={repaired}", Run: IsLogEnabled);
        }
    }

    /// <summary>Commits one immutable snapshot; a locked primary falls back to a durable sidecar read on the next launch.</summary>
    /// <param name="snapshot">The existing settings engine's complete serialized state.</param>
    internal static void SaveSnapshot(string snapshot)
    {
        lock (Gate)
        {
            string primary = AppConstants.ConfigFileName;
            string pending = primary + ".pending";
            try
            {
                FileRetry().Execute(() => SettingsManager.SaveSnapshot(snapshot, primary));
                if (File.Exists(pending))
                {
                    try { File.Delete(pending); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ExceptionBoundary.LogSafely($"Obsolete pending settings retained; primary is newer; error={error}", Run: IsLogEnabled); }
                }
                ExceptionBoundary.LogSafely($"Settings committed; path={primary}; characters={snapshot.Length}", Run: IsLogEnabled);
            }
            catch (Exception primaryError) when (primaryError is IOException or UnauthorizedAccessException or AggregateException)
            {
                ExceptionBoundary.LogSafely($"Primary settings write failed; preserving a pending snapshot; error={primaryError}", Run: IsLogEnabled);
                try
                {
                    FileRetry().Execute(() => SettingsManager.SaveSnapshot(snapshot, pending));
                    ExceptionBoundary.LogSafely($"Settings persisted through pending snapshot; path={pending}; characters={snapshot.Length}", Run: IsLogEnabled);
                }
                catch (Exception pendingError)
                {
                    throw new AggregateException("Both portable settings destinations failed.", primaryError, pendingError);
                }
            }
        }
    }

    /// <summary>Copies the user's invalid file into UserData before any normalized replacement.</summary>
    /// <param name="path">Existing settings or pending snapshot to retain.</param>
    internal static void Preserve(string path)
    {
        string folder = Path.Combine(AppPaths.UserData, "Backups");
        Directory.CreateDirectory(folder);
        string backup = Path.Combine(folder, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".previous");
        FileRetry().Execute(() => File.Copy(path, backup));
        ExceptionBoundary.LogSafely($"Original settings preserved; source={path}; backup={backup}", Run: IsLogEnabled);
    }

    /// <summary>Retries transient file-access failures without multiplying the caller's write attempts.</summary>
    /// <returns>The single portable-settings I/O retry policy.</returns>
    static Polly.Retry.RetryPolicy FileRetry() => Policy.Handle<IOException>(error => error is not PathTooLongException and not DriveNotFoundException)
        .WaitAndRetry(RetryCount, attempt => TimeSpan.FromMilliseconds(25 * attempt),
            (error, delay, attempt, context) => ExceptionBoundary.LogSafely($"Settings I/O retry; attempt={attempt}; delayMs={delay.TotalMilliseconds}; error={error}", Run: IsLogEnabled));
}
