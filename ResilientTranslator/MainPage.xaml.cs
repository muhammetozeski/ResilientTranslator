namespace ResilientTranslator
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            blazorWebView.BlazorWebViewInitializing += static (_, args) => args.UserDataFolder = Path.Combine(MainProject.Infrastructure.AppPaths.AppCache, "WebView2");
        }
    }
}

