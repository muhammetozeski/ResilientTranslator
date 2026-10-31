using ResilientTranslator.MainProject.Constants.ThemeConstants;
namespace ResilientTranslator.MainProject.UI.Elements.Core;

/// <summary>Owns transient interaction state for one rendered layout, not for the whole process.</summary>
public sealed class PopupScope
{
    object? TooltipOwner;
    Action? CloseTooltip;
    public bool KeyboardNavigation {get;set;}
    public bool TooltipsEnabled {get;private set;}=true;
    public int TooltipDelay {get;private set;}=AppMeasures.Tooltip.OpenDelay;
    public int TooltipFade {get;private set;}=AppMeasures.Tooltip.FadeDuration;
    public void ConfigureTooltips(bool enabled,int delayMilliseconds,int fadeMilliseconds)
    {
        if(delayMilliseconds<0||fadeMilliseconds<0)throw new ArgumentOutOfRangeException(nameof(delayMilliseconds));
        if(TooltipsEnabled==enabled&&TooltipDelay==delayMilliseconds&&TooltipFade==fadeMilliseconds)return;
        TooltipsEnabled=enabled;TooltipDelay=delayMilliseconds;TooltipFade=fadeMilliseconds;DismissTooltip();
    }
    public long InteractionVersion {get;private set;}
    public event Action? ViewportChanged;
    public void ActivateTooltip(object owner,Action close)
    {
        if(TooltipOwner==owner)return;
        var previous=CloseTooltip;TooltipOwner=owner;CloseTooltip=close;previous?.Invoke();
    }
    public void ReleaseTooltip(object owner){if(TooltipOwner==owner){TooltipOwner=null;CloseTooltip=null;}}
    public void DismissTooltip(){InteractionVersion++;var close=CloseTooltip;TooltipOwner=null;CloseTooltip=null;close?.Invoke();}
    public void Resize(){DismissTooltip();ViewportChanged?.Invoke();}
}
