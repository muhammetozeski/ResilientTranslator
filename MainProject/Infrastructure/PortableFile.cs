using System.Diagnostics;
using System.Text;

namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Atomically replaces portable text files without masking a write error during temporary-file cleanup.</summary>
internal static class PortableFile
{
    const bool IsLogEnabled = true;

    /// <summary>Writes a sibling temporary file and replaces the target, with a same-volume move fallback.</summary>
    /// <param name="path">The final absolute or resolvable text-file path.</param>
    /// <param name="content">The complete immutable UTF-8 content.</param>
    internal static void WriteAllText(string path, string content)
    {
        string target = Path.GetFullPath(path);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var timer = Stopwatch.StartNew();
        Exception? failure = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (!File.Exists(target)) File.Move(temporary, target);
            else
            {
                try { File.Replace(temporary, target, null); }
                catch (IOException replacementError) when (File.Exists(temporary))
                {
                    ExceptionBoundary.LogSafely($"Atomic replace unavailable; trying same-volume move; path={target}; error={replacementError}", IsLogEnabled);
                    try { File.Move(temporary, target, overwrite: true); }
                    catch (Exception moveError) { throw new AggregateException("Both atomic replacement methods failed.", replacementError, moveError); }
                }
            }
            ExceptionBoundary.LogSafely($"Portable text committed; path={target}; characters={content.Length}", Run: IsLogEnabled);
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception cleanupError)
            {
                ExceptionBoundary.LogSafely($"Temporary text cleanup failed; path={temporary}; error={new AggregateException(new[] { failure, cleanupError }.OfType<Exception>())}", IsLogEnabled);
            }
            ExceptionBoundary.LogSafely($"Portable text write finished; path={target}; failed={failure is not null}; elapsedMs={timer.Elapsed.TotalMilliseconds:F1}", Run: IsLogEnabled);
        }
    }
}
