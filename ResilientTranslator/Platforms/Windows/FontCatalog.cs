using System.Diagnostics;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ResilientTranslator.MainProject.Infrastructure;
using WpfFonts = System.Windows.Media.Fonts;

namespace ResilientTranslator;
/// <summary>Enumerates installed UI fonts on demand, with a GDI fallback and no persisted font-list cache.</summary>
internal static class FontCatalog
{
    const bool IsLogEnabled = true;
    /// <summary>Reads names on a dedicated worker and abandons only private enumeration data when canceled.</summary>
    /// <param name = "token">Cancels enumeration checkpoints and the caller's wait.</param>
    /// <returns>Distinct installed family names; no list is written to disk or the log.</returns>
    internal static async Task<string[]> LoadAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return await Resilience.ExecuteResilientlyAsync(inner =>
        {
            var pending = Task.Factory.StartNew(() => Read(inner), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _ = pending.ContinueWith(task => ExceptionBoundary.LogSafely($"Late font enumeration failure; error={task.Exception}", IsLogEnabled), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            return pending.WaitAsync(inner);
        }, error => error is IOException or ExternalException || error is AggregateException aggregate && aggregate.InnerExceptions.Any(item => item is IOException or ExternalException), token, retryCount: 1, attemptTimeout: TimeSpan.FromSeconds(10), totalTimeout: TimeSpan.FromSeconds(15), operationName: "InstalledFontEnumeration").WaitAsync(token).ConfigureAwait(false);
    }

    /// <summary>Uses the WPF font catalog, then an independent installed-font provider if it fails.</summary>
    /// <param name = "token">Cancels this operation&apos;s checkpoints and bounded wait.</param>
    /// <returns>Installed family names from the primary or fallback font provider.</returns>
    static string[] Read(CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            try
            {
                var names = new List<string>();
                foreach (var family in WpfFonts.SystemFontFamilies)
                {
                    token.ThrowIfCancellationRequested();
                    names.Add(family.Source);
                }

                return Complete(names, "WPF", token);
            }
            catch (Exception primaryError)when (primaryError is not OperationCanceledException and not OutOfMemoryException)
            {
                ExceptionBoundary.LogSafely($"WPF font enumeration failed; trying GDI; error={primaryError}", IsLogEnabled);
                try
                {
                    using var collection = new InstalledFontCollection();
                    var families = collection.Families;
                    try
                    {
                        return Complete(families.Select(family => family.Name), "GDI", token);
                    }
                    finally
                    {
                        foreach (var family in families)
                            family.Dispose();
                    }
                }
                catch (Exception fallbackError)when (fallbackError is not OperationCanceledException)
                {
                    throw new AggregateException("Both installed-font providers failed.", primaryError, fallbackError);
                }
            }
        }
        finally
        {
            Log($"Font enumeration finished; elapsedMs={timer.Elapsed.TotalMilliseconds:F1}", Run: IsLogEnabled);
        }
    }

    /// <summary>Rejects empty provider output and materializes only the caller's private array.</summary>
    /// <param name = "names">The font provider&apos;s private family-name enumeration.</param>
    /// <param name = "provider">The installed-font source named in diagnostics.</param>
    /// <param name = "token">Cancels this operation&apos;s checkpoints and bounded wait.</param>
    /// <returns>A private sorted array of distinct installed font-family names.</returns>
    static string[] Complete(IEnumerable<string> names, string provider, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string[] result = names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (result.Length == 0)
            throw new InvalidDataException("The installed-font provider returned no families.");
        Log($"Font families available; provider={provider}; count={result.Length}", Run: IsLogEnabled);
        return result;
    }
}
