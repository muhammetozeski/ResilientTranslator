using System.Text.Json;

namespace ResilientTranslator.MainProject.Infrastructure.Input;

[Flags] public enum ShortcutModifiers { None=0, Control=1, Shift=2, Alt=4, Windows=8 }
public enum ShortcutMouse { Left, Right, Middle, Mouse4, Mouse5 }

/// <summary>Stores physical keys and buttons; serialized names are configuration only.</summary>
public sealed record ShortcutChord(ShortcutModifiers Modifiers,ushort[] Keys,ShortcutMouse[] Buttons)
{
    public bool IsValid=>Keys.Length+Buttons.Length>0 && Keys.Length<=6 && Buttons.Length<=5
        && Keys.All(key=>key>0 && key<255) && Keys.Distinct().Count()==Keys.Length
        && Buttons.All(Enum.IsDefined) && Buttons.Distinct().Count()==Buttons.Length
        && (Modifiers & ~(ShortcutModifiers.Control|ShortcutModifiers.Shift|ShortcutModifiers.Alt|ShortcutModifiers.Windows))==0
        && !(Keys.Length==0 && Modifiers==ShortcutModifiers.None && Buttons.SequenceEqual([ShortcutMouse.Left]));
    public string Serialize()=>JsonSerializer.Serialize(this);
    public static ShortcutChord Parse(string value)
    {
        var chord=JsonSerializer.Deserialize<ShortcutChord>(value)??throw new FormatException("Missing shortcut chord.");
        if(chord.Keys is null || chord.Buttons is null || !chord.IsValid)throw new FormatException("Invalid shortcut chord.");
        return chord;
    }
}

/// <summary>Captures physical chords and applies native registration while retaining the last valid shortcut.</summary>
public interface IGlobalShortcut
{
    ShortcutChord Current {get;}
    ShortcutChord? Candidate {get;}
    bool IsCapturing {get;}
    event Action? Changed;
    string Display(ShortcutChord chord);
    Task BeginCaptureAsync();
    Task EndCaptureAsync(bool accept);
}
