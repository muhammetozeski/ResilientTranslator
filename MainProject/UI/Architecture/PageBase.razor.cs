using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace ResilientTranslator.MainProject.UI.Layout.Architecture
{
    /// <summary>Owns shared navigation state and the cascading palette received by routed pages.</summary>
    public partial class PageBase : LayoutComponentBase, IDisposable
    {
        const bool IsLogEnabled = true;
        [CascadingParameter] public ResilientTranslator.MainProject.Constants.ThemeConstants.AppTheme? ActiveTheme { get; set; }
        [Inject] public NavigationManager NavManager { get; set; } = default!;

        public bool HasNavigatedAway { get; private set; }

        // Auth Requirements
        [Parameter] public bool RequireAuth { get; set; }
        [Parameter] public bool RequireOnboarding { get; set; }

        /// <summary>Connects this component&apos;s lifecycle subscriptions before it handles application events.</summary>
        protected override void OnInitialized()
        {
            NavManager.LocationChanged += OnLocationChanged;
        }

        /// <summary>Records navigation away without leaving an anonymous event subscription behind.</summary>
        /// <param name="sender">The framework object delivering this callback.</param>
        /// <param name="args">Framework callback arguments, or the original process arguments for an entry point.</param>
        void OnLocationChanged(object? sender, LocationChangedEventArgs args) => HasNavigatedAway = true;

        /// <summary>Releases navigation ownership when a page instance is retired.</summary>
        public virtual void Dispose() => NavManager.LocationChanged -= OnLocationChanged;
        /// <summary>Reloads the root route through the existing navigation manager.</summary>
        protected void RecoverFromError()
        {
            Log("Shared page recovery requested; route=/",Run:IsLogEnabled);
            NavManager.NavigateTo("/", true);
        }
    }
}
