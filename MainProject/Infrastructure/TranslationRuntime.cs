using Microsoft.JSInterop;
using ResilientTranslator.MainProject.Infrastructure.Offline;
using ResilientTranslator.MainProject.Infrastructure.Detection;
namespace ResilientTranslator.MainProject.Infrastructure;

/// <summary>Maps the single persistent registry to retained local inference workers.</summary>
public static class TranslationRuntime
{
    public static async Task ConfigureAsync(CancellationToken token=default)
    {
        await OfflineTranslationBridge.ConfigureAsync(new OfflineEngineOptions{
            BergamotWorkers=Settings.BergamotWorkers.Value,BergamotCachedDirections=Settings.BergamotCachedDirections.Value,TozWorkers=Settings.TozWorkers.Value,TozThreads=Settings.TozThreads.Value,
            BergamotBeam=Settings.BergamotBeam.Value,TozBeam=Settings.TozBeam.Value,ModelLoadTimeoutMs=Settings.ModelLoadTimeout.Value,
            TranslationTimeoutMs=Settings.LocalTranslationTimeout.Value},token);
        await DetectorRegistry.ConfigureAsync(new DetectorOptions(Settings.DetectorModelLoadTimeout.Value,Settings.DetectorTimeout.Value,Settings.DetectorMaximumCharacters.Value),token);
    }
    public static async Task InitializeAsync(IJSRuntime js)
    {
        await ConfigureAsync();
        await Task.WhenAll(OfflineTranslationBridge.InitializeAsync(js),DetectorRegistry.InitializeAsync(js));
    }
    public static Task WarmAsync(CancellationToken token=default)
    {
        var source=Services.TranslationWorkspace.SourceLanguage;
        var target=Services.TranslationWorkspace.TargetLanguage;
        if(source==TranslationLanguageRules.Automatic)source=Settings.AutoFirstLanguage.Value;
        if(target==TranslationLanguageRules.Automatic)target=Settings.AutoSecondLanguage.Value;
        return OfflineTranslationBridge.WarmAsync(source,target,token);
    }
}
