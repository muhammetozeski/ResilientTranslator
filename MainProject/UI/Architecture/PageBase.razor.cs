using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace ResilientTranslator.MainProject.UI.Layout.Architecture
{
    public partial class PageBase : LayoutComponentBase, IDisposable
    {
        [CascadingParameter] public ResilientTranslator.MainProject.Constants.ThemeConstants.AppTheme? ActiveTheme { get; set; }
        [Inject] public NavigationManager NavManager { get; set; } = default!;

        public bool HasNavigatedAway { get; private set; }

        // Auth Requirements
        [Parameter] public bool RequireAuth { get; set; }
        [Parameter] public bool RequireOnboarding { get; set; }

        protected override void OnInitialized()
        {
            NavManager.LocationChanged += OnLocationChanged;
        }

        /// <summary>Records navigation away without leaving an anonymous event subscription behind.</summary>
        void OnLocationChanged(object? sender, LocationChangedEventArgs args) => HasNavigatedAway = true;

        /// <summary>Releases navigation ownership when a page instance is retired.</summary>
        public virtual void Dispose() => NavManager.LocationChanged -= OnLocationChanged;
        protected void RecoverFromError()
        {
            NavManager.NavigateTo("/", true);
        }
    }
}
