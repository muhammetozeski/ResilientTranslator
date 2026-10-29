using ResilientTranslator.MainProject.Constants;
namespace ResilientTranslator
{
    /// <summary>Creates the resident MAUI translator window from its retained browser page.</summary>
    public partial class App : Application
    {
        /// <summary>Loads MAUI application resources before a native window is created.</summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>Creates the translator&apos;s native window around its shared browser page.</summary>
        /// <param name="activationState">Framework activation metadata for the new MAUI window.</param>
        /// <returns>The translator window that owns the shared browser page.</returns>
        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new MainPage()) { Title = AppConstants.AppName };
        }
    }
}
