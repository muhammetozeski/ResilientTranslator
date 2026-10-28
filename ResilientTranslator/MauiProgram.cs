using Microsoft.Maui.LifecycleEvents;
using Microsoft.Extensions.Logging;

namespace ResilientTranslator
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(MainProject.Infrastructure.AppPaths.AppCache,"WebView2"));
            if (!MainProject.Infrastructure.ApplicationServices.Initialize().Succeeded) throw new InvalidOperationException("Application initialization failed.");
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
            builder.ConfigureLifecycleEvents(events => events.AddWindows(windows => windows.OnWindowCreated(DesktopHost.Attach)));


            return builder.Build();
        }
    }
}



