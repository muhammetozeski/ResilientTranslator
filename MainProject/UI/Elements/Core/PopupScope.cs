namespace ResilientTranslator.MainProject.UI.Elements.Core;

/// <summary>Owns transient interaction state for one rendered layout, not for the whole process.</summary>
public sealed class PopupScope
{
    object? TooltipOwner;
    Action? CloseTooltip;
    public bool KeyboardNavigation {get;set;}
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
