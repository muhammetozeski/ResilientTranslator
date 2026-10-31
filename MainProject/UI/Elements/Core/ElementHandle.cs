using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
namespace ResilientTranslator.MainProject.UI.Elements.Core;

/// <summary>A viewport-relative geometry snapshot; browser layout cost is separate from reference lookup.</summary>
public sealed record ElementBounds(double X,double Y,double Width,double Height,double ViewportWidth,double ViewportHeight)
{
    public double Right=>X+Width;
    public double Bottom=>Y+Height;
    public bool Contains(double x,double y)=>x>=X&&x<=Right&&y>=Y&&y<=Bottom;
}
public enum ElementPositionMode { Absolute, Fixed }
/// <summary>A viewport-relative point, usable by cursor tooltips and context menus.</summary>
public sealed record PopupPoint(double X,double Y);

/// <summary>Retains a direct browser-object handle after binding, without repeated selector searches in application code.</summary>
public sealed class ElementHandle(IJSRuntime javascript,Func<ElementReference> currentElement):IAsyncDisposable
{
    const bool IsLogEnabled=true;
    readonly SemaphoreSlim Gate=new(1,1);
    IJSObjectReference? Module,Handle;
    ElementReference Bound;
    bool Disposed;
    public async ValueTask<ElementBounds> GetBoundsAsync(CancellationToken token=default)
    {
        await Gate.WaitAsync(token);
        try{var bounds=await (await BindAsync(token)).InvokeAsync<ElementBounds>("bounds",token);Log($"Native bounds read; bounds={bounds}",Run:IsLogEnabled);return bounds;}
        finally{Gate.Release();}
    }
    public async ValueTask SetPositionAsync(double x,double y,ElementPositionMode mode=ElementPositionMode.Fixed,CancellationToken token=default)
    {
        if(!double.IsFinite(x)||!double.IsFinite(y))throw new ArgumentOutOfRangeException(nameof(x));
        await Gate.WaitAsync(token);
        try{await (await BindAsync(token)).InvokeVoidAsync("position",token,x,y,mode==ElementPositionMode.Fixed?"fixed":"absolute");}
        finally{Gate.Release();}
    }
    async ValueTask<IJSObjectReference> BindAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Disposed,this);
        var element=currentElement();
        if(Handle is not null&&Bound.Equals(element))return Handle;
        if(Handle is not null){await Handle.DisposeAsync();Handle=null;}
        Module??=await javascript.InvokeAsync<IJSObjectReference>("import",token,"./_content/MainProject/ui/element.js");
        Handle=await Module.InvokeAsync<IJSObjectReference>("bind",token,element);Bound=element;Log("Native element reference bound",Run:IsLogEnabled);
        return Handle;
    }
    public async ValueTask DisposeAsync()
    {
        await Gate.WaitAsync();
        try{if(Disposed)return;Disposed=true;try{if(Handle is not null)await Handle.DisposeAsync();}finally{if(Module is not null)await Module.DisposeAsync();}}
        finally{Gate.Release();}
    }
}
