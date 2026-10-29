using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace ResilientTranslator;
/// <summary>Hosts the retained browser and supplies native keyboard focus before DOM editor focus.</summary>
public partial class MainPage : ContentPage
{
    const bool IsLogEnabled=true;
    /// <summary>Loads the retained Blazor host and connects actual browser keyboard focus.</summary>
    public MainPage()
    {
        InitializeComponent();
        MainProject.Services.TranslationWorkspace.FocusWebView=FocusWebViewAsync;
    }
    /// <summary>Moves actual keyboard focus into the browser host on its owning UI thread.</summary>
    /// <param name="token">Cancels this operation&apos;s checkpoints and bounded wait.</param>
    /// <returns>Completion of actual keyboard focus transfer into the browser host.</returns>
    Task FocusWebViewAsync(CancellationToken token) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        token.ThrowIfCancellationRequested();
        bool focused=blazorWebView.Handler?.PlatformView is WebView2 native && native.Focus(FocusState.Programmatic);
        if(!focused) focused=blazorWebView.Focus();
        Log($"Native WebView focus requested; accepted={focused}",Run:IsLogEnabled);
        if(!focused) throw new InvalidOperationException("The browser host rejected keyboard focus.");
    });
    /// <summary>Installs native frame input before the retained Blazor document is shown.</summary>
    /// <param name="sender">The framework object delivering this callback.</param>
    /// <param name="args">Framework callback arguments, or the original process arguments for an entry point.</param>
    async void OnWebViewInitialized(object? sender,Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs args)
    {
        await MainProject.Infrastructure.ExceptionBoundary.RunSafelyAsync(async token =>
            await DesktopHost.AttachBrowserAsync(args.WebView.CoreWebView2).WaitAsync(token),operationName:"AttachNativeBrowserInput");
    }}
