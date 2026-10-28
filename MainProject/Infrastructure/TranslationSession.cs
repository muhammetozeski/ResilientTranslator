using System.Reflection;
using System.Runtime.Loader;
namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Owns one unchanged engine instance and disposes its connections when that attempt ends or is canceled.</summary>
internal sealed class TranslationSession : IDisposable
{
    const bool IsLogEnabled = true;
    EngineContext? Context;
    HttpClient[] Clients=[];
    readonly Func<string?,string,string,bool,Task<(string? Text,Exception? Error,string Function,string Service)>> Translate;
    readonly long Request;
    readonly int Attempt;
    /// <summary>Loads an independent provider state so cleanup cannot cancel the next attempt.</summary>
    internal TranslationSession(long request,int attempt)
    {
        Request=request; Attempt=attempt;
        string directory=Path.GetDirectoryName(typeof(TranslationSession).Assembly.Location)!;
        var context=new EngineContext(directory); Context=context;
        try
        {
            Type engine=context.LoadFromAssemblyPath(Path.Combine(directory,"TranslationEngine.dll")).GetType("ResilientTranslator",throwOnError:true)!;
            var clients=new HashSet<HttpClient>();
            foreach(var field in engine.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
            {
                object? instance=field.GetValue(null);
                if(instance is HttpClient client) clients.Add(client);
                else if(instance?.GetType().Namespace?.StartsWith("GTranslate",StringComparison.Ordinal)==true)
                    foreach(var member in instance.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                        if(member.GetValue(instance) is HttpClient providerClient) clients.Add(providerClient);
            }
            Clients=[..clients];
            Translate=engine.GetMethod("TranslateAsync",BindingFlags.Public | BindingFlags.Static)!
                .CreateDelegate<Func<string?,string,string,bool,Task<(string?,Exception?,string,string)>>>();
            ExceptionBoundary.LogSafely($"Translation session opened; request={Request}; attempt={Attempt}; clients={Clients.Length}",IsLogEnabled);
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Passes the same inner cancellation token to cleanup and the bounded wait around the original API.</summary>
    internal async Task<ConfirmedTranslation> ExecuteAsync(TranslationAttempt input,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var scope=EngineTrace.Enter(Request,Attempt,input.Mode);
        using var registration=token.Register(Dispose);
        Task<(string? Text,Exception? Error,string Function,string Service)> pending=Translate(input.Text,input.SourceLanguage,input.TargetLanguage,input.Mode==TranslationMode.Concurrent);
        _=pending.ContinueWith(task => ExceptionBoundary.LogSafely($"Late engine failure; request={Request}; attempt={Attempt}; error={task.Exception}",IsLogEnabled),CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        var result=await pending.WaitAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if(result.Error is not null) throw new TranslationServicesException(result.Error);
        if(string.IsNullOrWhiteSpace(result.Text)) throw new InvalidDataException("The engine returned an empty successful result.");
        return new(result.Text,result.Function,result.Service);
    }
    /// <summary>Closes this attempt's clients once; cleanup failures are logged without replacing the original failure.</summary>
    public void Dispose()
    {
        foreach(var client in Interlocked.Exchange(ref Clients,[]))
        {
            try { client.CancelPendingRequests(); client.Dispose(); }
            catch(Exception error) { ExceptionBoundary.LogSafely($"Translation client shutdown failed; request={Request}; attempt={Attempt}; error={error}",IsLogEnabled); }
        }
        var context=Interlocked.Exchange(ref Context,null);
        if(context is not null)
        {
            try { context.Unload(); }
            catch(Exception error) { ExceptionBoundary.LogSafely($"Engine unload failed; request={Request}; attempt={Attempt}; error={error}",IsLogEnabled); }
            ExceptionBoundary.LogSafely($"Translation session closed; request={Request}; attempt={Attempt}",IsLogEnabled);
        }
    }
    sealed class EngineContext(string directory) : AssemblyLoadContext(isCollectible:true)
    {
        protected override Assembly? Load(AssemblyName name) => name.Name is "TranslationEngine" or "GTranslate" or "Polly" or "Polly.Core"
            ? LoadFromAssemblyPath(Path.Combine(directory,name.Name+".dll")) : null;
    }
}

