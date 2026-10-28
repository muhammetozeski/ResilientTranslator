using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Logger;
namespace ResilientTranslator;
/// <summary>Hosts the retained browser and supplies native keyboard focus before DOM editor focus.</summary>
public partial class MainPage : ContentPage
{
    const bool IsLogEnabled=true;
    public MainPage()
    {
        InitializeComponent();
        MainProject.Services.TranslationWorkspace.FocusWebView=FocusWebViewAsync;
    }
    /// <summary>Moves actual keyboard focus into the browser host on its owning UI thread.</summary>
    Task FocusWebViewAsync(CancellationToken token) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        token.ThrowIfCancellationRequested();
        bool focused=blazorWebView.Handler?.PlatformView is WebView2 native && native.Focus(FocusState.Programmatic);
        if(!focused) focused=blazorWebView.Focus();
        Log($"Native WebView focus requested; accepted={focused}",Run:IsLogEnabled);
        if(!focused) throw new InvalidOperationException("The browser host rejected keyboard focus.");
    });
}
