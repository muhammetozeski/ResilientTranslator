using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ResilientTranslator.MainProject.UI.Layout.Architecture;
namespace ResilientTranslator.MainProject.UI.Elements.Core;

/// <summary>Gives controls with one designated native element shared geometry, positioning and focus APIs.</summary>
public class CoreElement:ThemeComponent,IAsyncDisposable
{
    [Inject] protected IJSRuntime ElementJavaScript {get;set;}=default!;
    protected ElementReference NativeElement;
    [CascadingParameter] protected CoreTooltip? ParentTooltip {get;set;}
    [CascadingParameter] protected ControlDescription? FieldDescription {get;set;}
    protected readonly string DescriptionId="tip-"+Guid.NewGuid().ToString("N");
    protected string Description(IReadOnlyDictionary<string,object>? attributes)
    {
        if(attributes?.TryGetValue("data-tooltip",out var explicitHelp)==true)return explicitHelp?.ToString()??"";
        if(FieldDescription is not null)return FieldDescription.Text;
        foreach(var key in new[]{"title","aria-label"})if(attributes?.TryGetValue(key,out var text)==true && !string.IsNullOrWhiteSpace(text?.ToString()))return text.ToString()!;
        return "";
    }
    protected bool NeedsTooltip(IReadOnlyDictionary<string,object>? attributes)=>ParentTooltip is null&&Description(attributes).Length>0;
    protected IReadOnlyDictionary<string,object>? HtmlAttributes(IReadOnlyDictionary<string,object>? attributes)
    {
        if(attributes is null)return null;
        var result=new Dictionary<string,object>(attributes);result.Remove("title");
        if(ParentTooltip is not null)result["aria-describedby"]=ParentTooltip.Id;
        else if(NeedsTooltip(attributes))result["aria-describedby"]=DescriptionId;
        return result;
    }
    protected string NativeStyle(IReadOnlyDictionary<string,object>? attributes)=>WithPosition(attributes?.TryGetValue("style",out var style)==true?style?.ToString():null);
    ElementHandle? Handle;
    string PositionStyle="";
    public ElementHandle DomElement=>Handle??=new(ElementJavaScript,()=>NativeElement);
    public ValueTask<ElementBounds> GetBoundsAsync(CancellationToken token=default)=>DomElement.GetBoundsAsync(token);
    public async Task SetPositionAsync(double x,double y,ElementPositionMode mode=ElementPositionMode.Fixed,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        if(!double.IsFinite(x)||!double.IsFinite(y))throw new ArgumentOutOfRangeException(nameof(x));
        PositionStyle=string.Create(CultureInfo.InvariantCulture,$"position:{(mode==ElementPositionMode.Fixed?"fixed":"absolute")};left:{x}px;top:{y}px;");
        await InvokeAsync(StateHasChanged);
        await DomElement.SetPositionAsync(x,y,mode,token);
    }
    protected string WithPosition(string? style)=>$"{style};{PositionStyle}";
    public ValueTask FocusAsync(bool preventScroll=true)=>NativeElement.FocusAsync(preventScroll);
    public virtual async ValueTask DisposeAsync(){if(Handle is not null)await Handle.DisposeAsync();}
}
