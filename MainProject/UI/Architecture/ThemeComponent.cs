using Microsoft.AspNetCore.Components;
using ResilientTranslator.MainProject.Constants.ThemeConstants;

namespace ResilientTranslator.MainProject.UI.Layout.Architecture;

/// <summary>Receives the root palette so existing control and CSS instances rerender without losing their state.</summary>
public class ThemeComponent : ComponentBase
{
    [CascadingParameter] public AppTheme? ActiveTheme { get; set; }
}
