using Microsoft.Maui.LifecycleEvents;
using Microsoft.Extensions.Logging;

namespace ResilientTranslator
{
    /// <summary>Builds the Windows host after portable settings and diagnostics are initialized.</summary>
    public static class MauiProgram
    {
        /// <summary>Builds the MAUI service host and connects the shared UI to Windows lifecycle callbacks.</summary>
        /// <returns>The configured MAUI host, ready for native window creation.</returns>
        public static MauiApp CreateMauiApp()
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(MainProject.Infrastructure.AppPaths.AppCache,"WebView2"));
            MainProject.Infrastructure.EngineTrace.Initialize();
            var initialization = MainProject.Infrastructure.ApplicationServices.Initialize();
            if (!initialization.Succeeded) throw new InvalidOperationException("Application initialization failed.", initialization.Error);
            MainProject.Infrastructure.Preferences.Initialize();
            MainProject.Infrastructure.EngineTrace.Initialize();
            MainProject.Services.TranslationWorkspace.Initialize();
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddSingleton<MainProject.Infrastructure.Speech.IEnglishPhonemizer, EnglishPhonemizer>();
            builder.Services.AddScoped<MainProject.Infrastructure.Speech.SpeechPlayback>();
            builder.ConfigureLifecycleEvents(events => events.AddWindows(windows => windows.OnWindowCreated(DesktopHost.Attach)));


            return builder.Build();
        }
    }
}




