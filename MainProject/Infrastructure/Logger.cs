global using static Logger;
using System.Collections.Concurrent;
using Polly;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ResilientTranslator.MainProject.Infrastructure;

#pragma warning disable CA1050 // Ad alanlarında türleri bildirin
/// <summary>Queues portable diagnostic records and retains a bounded in-memory view.</summary>
public static class Logger
#pragma warning restore CA1050 // Ad alanlarında türleri bildirin
{
    // bu projede logger publish'den önce kapatılmalıdır
    /// <summary> enable or disable logging by changing this </summary>
    static int _activateLogging = 1; // AppConstants.TestBuild kaldırıldığı için varsayılan olarak aktif
    public static bool ActivateLogging
    {
        get => Interlocked.CompareExchange(ref _activateLogging, 0, 0) == 1;
        set => Interlocked.Exchange(ref _activateLogging, value ? 1 : 0);
    }

    const bool WriteToDisk = true; // Bunu false yaparak diske yazmayı devre dışı bırakabilirsin, bu sayede sadece konsola loglama yapar

    const int MaximumBufferedRecords = 2000;
    const int MaximumBufferedCharacters = 4 * 1024 * 1024;
    static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);
    static readonly object MemoryGate = new();
    static long BufferedCharacters;
    static readonly object QueueGate = new();
    static volatile bool AcceptingRecords = true;

    static readonly Action<object?> WriteLine = static (o) => Debug.WriteLine(o);
    static readonly Action<object?> Write = static (o) => Debug.Write(o);


    // Eğer dinamik olarak değistirmen gerekirse Const olmaktan çıkartabilirsin, bunu yapmak güvenli.
    const bool PrintDebugModStyle = true;

    public static readonly string startTime = DateTime.UtcNow.AddHours(3).ToString("yyyy.MM.dd HH.mm.ss.ff");

    public static readonly string LogsFolder = Path.Combine(AppPaths.AppCache, "Logs");
    public const string LogFileNamePrefix = "Log";
    public readonly static string LogFileName = LogsFolder + "\\" + LogFileNamePrefix + " " + startTime + ".txt";

    // enter -1 to disable
    const int DeleteOlderThanLastXFile = 3;

    public static readonly ConcurrentQueue<string> AllLogs = new();

    public static readonly ConcurrentQueue<string?> AllLogsUserFriendly = new();

    /// <summary> Assembles every buffered log line (from <see cref="AllLogs"/>) into one string. Used by the DevTools "copy logs" button so logs can be pulled off a device that has no debugger attached. </summary>
    /// <returns>The currently retained in-memory diagnostic records in their queue order.</returns>
    public static string GetAllLogsText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in AllLogs) sb.Append(line);
        return sb.ToString();
    }

    /// <summary> Empties the in-memory log buffer (DevTools), so a fresh capture starts clean. </summary>
    public static void ClearAllLogs()
    {
        lock (MemoryGate) { AllLogs.Clear(); AllLogsUserFriendly.Clear(); BufferedCharacters = 0; }
    }

    /// <summary>Associates one queued record or flush marker with its optional completion signal.</summary>
    /// <param name="Message">The complete diagnostic text or native Windows message identifier.</param>
    /// <param name="Completion">The optional queue-processing signal for this record or flush marker.</param>
    sealed record QueuedLog(string Message, TaskCompletionSource<bool>? Completion);
    static readonly BlockingCollection<QueuedLog> _logQueue = [];

    /// <summary>Starts the background portable log writer and applies file retention.</summary>
    static Logger()
    {
        if (!ActivateLogging)
            return;

        if (!string.IsNullOrWhiteSpace(LogsFolder) && WriteToDisk)
        {
            try { Directory.CreateDirectory(LogsFolder); } catch (Exception error) { Debug.WriteLine(error); }
        }

        if (DeleteOlderThanLastXFile > -1 && WriteToDisk)
#pragma warning disable CS0162 // Ulaşılamayan kod algılandı
            try
            {
                DeleteOldestFiles(LogsFolder, DeleteOlderThanLastXFile, LogFileNamePrefix);
            }
            catch (Exception e)
            {
                Log("An Error occured while deleting the old log files. The exception here:\n\n" + e);
            }

        if (WriteToDisk)
            new Thread(() =>
            {
                string? lastPath = null;
                string lastMessage = "";
                foreach (var (Message, Completion) in _logQueue.GetConsumingEnumerable())
                {
                    bool persisted = false;
                    try
                    {
                        if (Message.Length > 0) { lastMessage = Message; lastPath = WriteRecord(Message); }
                        persisted = Completion is null ? lastPath is not null : FlushRecord(lastPath,lastMessage);
                    }
                    catch (Exception error) { Debug.WriteLine($"Log writer failure: {error}"); }
                    finally { Completion?.TrySetResult(persisted); }
                }
            })
            { IsBackground = true }.Start();
#pragma warning restore CS0162 // Ulaşılamayan kod algılandı(
    }

    /// <summary>Repairs a missing log directory, retries a busy file, then preserves failures in the emergency log.</summary>
    /// <param name="message">The complete diagnostic text or native Windows message identifier.</param>
    /// <returns>The actual file that accepted the complete record, or null after every file sink failed.</returns>
    static string? WriteRecord(string message)
    {
        try
        {
            Directory.CreateDirectory(LogsFolder);
            File.AppendAllText(LogFileName,message+Environment.NewLine);
            return LogFileName;
        }
        catch(Exception initialError)
        {
            try
            {
                Directory.CreateDirectory(LogsFolder);
                Policy.Handle<IOException>().WaitAndRetry(2,attempt=>TimeSpan.FromMilliseconds(20*attempt))
                    .Execute(()=>File.AppendAllText(LogFileName,message+Environment.NewLine));
                return LogFileName;
            }
            catch(Exception retryError)
            {
                string record=$"{message}{Environment.NewLine}{new AggregateException(initialError,retryError)}{Environment.NewLine}";
                try
                {
                    string emergency=Path.Combine(AppPaths.AppCache,"Logger.Emergency.log");
                    Directory.CreateDirectory(AppPaths.AppCache);
                    File.AppendAllText(emergency,record);
                    return emergency;
                }
                catch(Exception emergencyError)
                {
                    try { Console.Error.WriteLine(record+emergencyError); } catch(Exception terminalError) { Debug.WriteLine(terminalError); }
                    return null;
                }
            }
        }
    }

    /// <summary>Flushes the actual final sink to disk, with an independent emergency append if the primary sink cannot be flushed.</summary>
    /// <param name="path">The file destination that accepted the most recent record.</param>
    /// <param name="message">The exact final record, retained for emergency durable writing.</param>
    /// <returns>True only after a primary or emergency file has been flushed to disk.</returns>
    static bool FlushRecord(string? path,string message)
    {
        if(message.Length==0) return true;
        try
        {
            if(path is null) throw new IOException("The last queued log record has no successful file destination.");
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);
            stream.Flush(flushToDisk:true);
            return true;
        }
        catch(Exception primaryError)
        {
            try
            {
                string emergency=Path.Combine(AppPaths.AppCache,"Logger.Emergency.log");
                Directory.CreateDirectory(AppPaths.AppCache);
                using var stream=new FileStream(emergency,FileMode.Append,FileAccess.Write,FileShare.ReadWrite);
                byte[] bytes=System.Text.Encoding.UTF8.GetBytes(message+Environment.NewLine+primaryError+Environment.NewLine);
                stream.Write(bytes);stream.Flush(flushToDisk:true);
                return true;
            }
            catch(Exception fallbackError){Debug.WriteLine(new AggregateException(primaryError,fallbackError));return false;}
        }
    }
    /// <summary>Associates a diagnostic level name with its display color.</summary>
    /// <param name="name">The native human-readable language or diagnostic label.</param>
    /// <param name="consoleColor">An optional display-color override for this diagnostic call.</param>
    public class LogLevel(string name, ConsoleColor consoleColor)
    {
        public static readonly LogLevel Info = new(nameof(Info), ConsoleColor.Blue);
        public static readonly LogLevel Debug = new(nameof(Debug), ConsoleColor.Green);
        public static readonly LogLevel Warning = new(nameof(Warning), ConsoleColor.Yellow);
        public static readonly LogLevel Error = new(nameof(Error), ConsoleColor.Red);

        public string Name { get; init; } = name;
        public ConsoleColor ConsoleColor { get; init; } = consoleColor;
    }

    /// <summary>
    /// Logs a message or object to the console and disk asynchronously. 
    /// Handles <see cref="System.Collections.IEnumerable"/> by expanding their contents and captures caller metadata automatically.
    /// </summary>
    /// <param name="MessageObject">The object or message to be logged.</param>
    /// <param name="consoleColor">The color of the text when printing to the <see cref="Console"/>.</param>
    /// <param name="PrintToConsole">If true, outputs the log to the standard console.</param>
    /// <!--<param name="WriteToDisk">If true, queues the log for persistent storage via <see cref="_logQueue"/>.</param>--> <!-- Bu parametre artık sabit bir alana taşındı, ancak yine de fonksiyon imzasında bırakıldı, böylece gelecekte dinamik olarak kontrol edilebilir. -->
    /// <param name="UseNewLine">If true, uses <see cref="WriteLine"/>; otherwise, uses <see cref="Write"/>.</param>
    /// <param name="WaitForLogging">If true, uses a bounded completion signal for durable writing; UI callers use the asynchronous completion API.</param>
    /// <param name="callerFunction">Automatically captured name of the member calling the method via <see cref="CallerMemberNameAttribute"/>.</param>
    /// <param name="callerFilePath">Automatically captured full path of the source file via <see cref="CallerFilePathAttribute"/>.</param>
    /// <param name="callerLine">Automatically captured line number via <see cref="CallerLineNumberAttribute"/>.</param>
    /// <param name="Run">A master switch to enable or disable the specific log call execution.</param>
    /// <returns>A string representation of the logged object, or null if <paramref name="MessageObject"/> is null.</returns>
    /// <param name="logLevel">The diagnostic severity and its default display color.</param>
    public static string? Log(object? MessageObject, LogLevel? logLevel = null, ConsoleColor? consoleColor = null, bool PrintToConsole = true, /*bool WriteToDisk = true,*/ bool UseNewLine = true, bool WaitForLogging = false,
        [CallerMemberName] string callerFunction = "",
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0, bool Run = true
        )
    {
        if (!ActivateLogging || !AcceptingRecords)
            return null;

        bool WriteToDisk = Logger.WriteToDisk;

        if (!Run || (!PrintToConsole && !WriteToDisk)) return MessageObject?.ToString();

        logLevel ??= LogLevel.Debug;




        string? returnValue = null;

        const string prefix = "> ";
        const string suffix = "\n-----------------------------\n\n";
        string CallerFile;
        try
        {
            CallerFile = Path.GetFileName(callerFilePath);
            if (string.IsNullOrWhiteSpace(CallerFile))
            {
                CallerFile = "null";
            }
        }
        catch (Exception e)
        {
            CallerFile = "\"exception: " + e.Message + "\"";
        }

        string now = DateTime.UtcNow.AddHours(3).ToString("dd.MM.yyyy HH.mm.ss.ff"); //I want Türkiye time zone style

        string Message = prefix + "[" + now + "] " +
            "[" + CallerFile + "/" + callerFunction + " Line: " + callerLine +
            " Thread Id: " + Environment.CurrentManagedThreadId + "]:\n[" + logLevel.Name + "] [" + (consoleColor ?? logLevel.ConsoleColor) + "] ";

        try
        {

            if (MessageObject is string literal)
            {
                returnValue = literal; Message += literal;
            }
            else if (MessageObject is System.Collections.IEnumerable numerable)
            {
                foreach (var item in numerable)
                {
                    try
                    {
                        string? itemString = item?.ToString();

                        if (returnValue == null)
                            returnValue = itemString ?? "null";
                        else
                            returnValue += itemString ?? "null";

                        returnValue += "\n";
                        Message += itemString ?? "item.ToString() in given numerable, has returned null\n";
                    }
                    catch (Exception e)
                    {
                        returnValue += "error";
                        Message += "An error occured while converting to string the given object in the list in the \"Log()\" function. The error:\n";
                        Message += e;
                        Message += "\n";
                    }
                }
            }
            else
            {
                returnValue = MessageObject?.ToString();
                Message += returnValue ?? "MessageObject.ToString() has returned null";
            }

        }
        catch (Exception e)
        {
            Message += "An error occured while converting to string the given object in the \"Log()\" function. The error:\n";
            Message += e;
        }

        Message += suffix;
        if (PrintToConsole)
        {

#pragma warning disable CS0162 // Ulaşılamayan kod algılandı
            if (PrintDebugModStyle)
            {
                if (UseNewLine) WriteLine(Message);
                else Write(Message);
            }
            else
            {
                if (UseNewLine) WriteLine(MessageObject);
                else Write(MessageObject);
            }
#pragma warning restore CS0162 // Ulaşılamayan kod algılandı

        }

        TaskCompletionSource<bool>? completion;
        lock(QueueGate)
        {
            if(!AcceptingRecords) return returnValue;
            Remember(Message,returnValue);
            completion=WaitForLogging ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) : null;
            if(WriteToDisk) _logQueue.Add(new(Message,completion));
        }
        if(WriteToDisk && completion is not null && !completion.Task.Wait(FlushTimeout))
            Debug.WriteLine("Synchronous log wait expired; the writer retains the queued record.");        return returnValue;
    }

    /// <summary>Waits asynchronously for records queued before this marker, without blocking the UI dispatcher.</summary>
    /// <param name="token">Cancels waiting; already queued records remain owned by the writer.</param>
    /// <returns>A bounded wait for queue processing; sink errors use the writer's emergency paths.</returns>
    public static Task<bool> FlushAsync(CancellationToken token = default)
    {
        if(!ActivateLogging || !WriteToDisk) return Task.FromResult(true);
        var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock(QueueGate) _logQueue.Add(new("",completion));
        return completion.Task.WaitAsync(FlushTimeout,token);
    }

    /// <summary>Queues the explicit last record, stops accepting later records and confirms its durable file flush.</summary>
    /// <param name="message">The final lifecycle diagnostic to persist before process termination.</param>
    /// <param name="token">Cancels waiting without discarding already queued records.</param>
    /// <returns>True only when enabled logging has durably flushed the final record.</returns>
    public static async Task<bool> CompleteAsync(string message,CancellationToken token=default)
    {
        Task<bool> pending;
        lock(QueueGate)
        {
            Log(message,PrintToConsole:false);
            AcceptingRecords=false;
            pending=FlushAsync(token);
        }
        try
        {
            bool persisted=await pending.ConfigureAwait(false);
            if(!persisted) ResumeAfterFailedShutdown();
            return persisted;
        }
        catch { ResumeAfterFailedShutdown();throw; }
    }

    /// <summary>Reopens diagnostics when native termination failed after the final record had been flushed.</summary>
    public static void ResumeAfterFailedShutdown() { lock(QueueGate) AcceptingRecords=true; }
    /// <summary>Keeps recent diagnostics within record and character budgets; full records still go to disk.</summary>
    /// <param name="message">The complete diagnostic text or native Windows message identifier.</param>
    /// <param name="friendly">The optional readable value retained beside the full diagnostic record.</param>
    static void Remember(string message, string? friendly)
    {
        if ((long)message.Length + (friendly?.Length ?? 0) > MaximumBufferedCharacters)
            message = friendly = $"Full diagnostic record stored in {LogFileName}; characters={message.Length}.";
        lock (MemoryGate)
        {
            AllLogs.Enqueue(message); AllLogsUserFriendly.Enqueue(friendly);
            BufferedCharacters += message.Length + (friendly?.Length ?? 0);
            while (AllLogs.Count > MaximumBufferedRecords || BufferedCharacters > MaximumBufferedCharacters)
            {
                if (!AllLogs.TryDequeue(out var removed)) break;
                AllLogsUserFriendly.TryDequeue(out var removedFriendly);
                BufferedCharacters -= removed.Length + (removedFriendly?.Length ?? 0);
            }
        }
    }
    /// <summary>Removes only dated diagnostic files beyond the configured retained count.</summary>
    /// <param name="folderPath">The diagnostic directory whose dated files are managed.</param>
    /// <param name="filesToKeep">The maximum count of dated diagnostic files to retain.</param>
    /// <param name="prefix">The filename prefix used to identify this application&apos;s dated logs.</param>
    static void DeleteOldestFiles(string folderPath, int filesToKeep, string prefix = "")
    {
        if (!Directory.Exists(folderPath))
        {
            Log("[ERROR] [DirectoryNotFoundException] File deletion operation: Folder not found. Folder path: " + folderPath);
            return; // Klasör bulunamazsa işlemden çık
        }

        var files = Directory.GetFiles(folderPath)
                             .Select(filePath => new
                             {
                                 Path = filePath,
                                 FileName = Path.GetFileNameWithoutExtension(filePath)
                             })
                             .ToList();

        var datedFiles = new List<(string Path, DateTime Date)>();

        foreach (var file in files)
        {
            string name = file.FileName;
            name = name.Contains(prefix) ? name.Remove(name.IndexOf(prefix), prefix.Length) : name;
            name = name.Trim();
            if (DateTime.TryParseExact(name, "yyyy.MM.dd HH.mm.ss.ff",
                                       System.Globalization.CultureInfo.InvariantCulture,
                                       System.Globalization.DateTimeStyles.None, out DateTime fileDate))
            {
                datedFiles.Add((file.Path, fileDate));
            }
            // Hatalı formatta dosya adları garanti edildiği için else bloğuna gerek yok.
        }

        var sortedFiles = datedFiles.OrderBy(f => f.Date).ToList();

        int filesToDeleteCount = sortedFiles.Count - filesToKeep;

        if (filesToDeleteCount <= 0)
        {
            return; // Silinecek dosya yok
        }

        for (int i = 0; i < filesToDeleteCount; i++)
        {
            File.Delete(sortedFiles[i].Path);
        }
    }
}

// [rule] yapay zekalar bu sınıftaki türkçe yorum satırlarına dokunmasın

