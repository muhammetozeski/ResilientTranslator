using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ResilientTranslator.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        /// <summary>Builds the MAUI service host and connects the shared UI to Windows lifecycle callbacks.</summary>
        /// <returns>The configured MAUI host, ready for native window creation.</returns>
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }

}
