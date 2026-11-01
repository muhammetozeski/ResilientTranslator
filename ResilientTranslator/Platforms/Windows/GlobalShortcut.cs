using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Maui.ApplicationModel;
using ResilientTranslator.MainProject.Infrastructure;
using ResilientTranslator.MainProject.Infrastructure.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using Forms=System.Windows.Forms;
using Preferences=ResilientTranslator.MainProject.Infrastructure.Preferences;

namespace ResilientTranslator;

/// <summary>Uses native hotkey registration for ordinary chords and event hooks for mouse/multi-key chords.</summary>
public sealed class GlobalShortcut : IGlobalShortcut
{
    const bool IsLogEnabled=true;
    public static GlobalShortcut Instance {get;}=new();
    readonly HOOKPROC KeyboardCallback, MouseCallback;
    readonly HashSet<ushort> DownKeys=[];
    readonly HashSet<ShortcutMouse> DownButtons=[];
    HHOOK KeyboardHook, MouseHook;
    HWND Receiver;
    Action? Toggle;
    bool Registered, Matched;
    public ShortcutChord Current {get;private set;}=DefaultChord();
    static ShortcutChord DefaultChord()=>new(ShortcutModifiers.Windows,[(ushort)VIRTUAL_KEY.VK_OEM_102],[]);
    public ShortcutChord? Candidate {get;private set;}
    public bool IsCapturing {get;private set;}
    public event Action? Changed;
    GlobalShortcut(){KeyboardCallback=Keyboard;MouseCallback=Mouse;}

    internal void Attach(HWND receiver,Action toggle)
    {
        Receiver=receiver;Toggle=toggle;
        Current=string.IsNullOrEmpty(Preferences.ShortcutConfiguration)?DefaultChord():ShortcutChord.Parse(Preferences.ShortcutConfiguration);
        try{Configure();}
        catch(Exception error)
        {
            ExceptionBoundary.LogSafely($"Configured shortcut unavailable; trying default; error={error}",IsLogEnabled);
            Current=DefaultChord();
            try{Configure();}
            catch(Exception fallbackError){ExceptionBoundary.LogSafely($"Shortcut registration failed; tray access remains available; error={new AggregateException(error,fallbackError)}",IsLogEnabled);}
        }
        Log($"Shortcut attached; chord={Display(Current)}",Run:IsLogEnabled);
    }
    public void Detach(){Unregister();ReleaseHooks();Receiver=default;Toggle=null;IsCapturing=false;}
    public bool HandleMessage(uint message,nuint id)
    {
        if(message!=PInvoke.WM_HOTKEY || id!=1 || !Registered || IsCapturing)return false;
        Toggle?.Invoke();return true;
    }
    void Unregister(){if(Registered){PInvoke.UnregisterHotKey(Receiver,1);Registered=false;}}
    unsafe void Configure()
    {
        Unregister();ReleaseHooks();DownKeys.Clear();DownButtons.Clear();Matched=false;
        if(!IsCapturing && Current.Keys.Length==1 && Current.Buttons.Length==0)
        {
            HOT_KEY_MODIFIERS modifiers=HOT_KEY_MODIFIERS.MOD_NOREPEAT;
            if(Current.Modifiers.HasFlag(ShortcutModifiers.Control))modifiers|=HOT_KEY_MODIFIERS.MOD_CONTROL;
            if(Current.Modifiers.HasFlag(ShortcutModifiers.Shift))modifiers|=HOT_KEY_MODIFIERS.MOD_SHIFT;
            if(Current.Modifiers.HasFlag(ShortcutModifiers.Alt))modifiers|=HOT_KEY_MODIFIERS.MOD_ALT;
            if(Current.Modifiers.HasFlag(ShortcutModifiers.Windows))modifiers|=HOT_KEY_MODIFIERS.MOD_WIN;
            if(!PInvoke.RegisterHotKey(Receiver,1,modifiers,Current.Keys[0]))throw new Win32Exception(Marshal.GetLastWin32Error());
            Registered=true;return;
        }
        var instance=new HINSTANCE(PInvoke.GetModuleHandle((string?)null).Value);
        KeyboardHook=PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL,KeyboardCallback,instance,0);
        MouseHook=PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL,MouseCallback,instance,0);
        if(KeyboardHook.IsNull || MouseHook.IsNull){ReleaseHooks();throw new Win32Exception(Marshal.GetLastWin32Error());}
        // One initial snapshot handles modifiers already held when recording starts; there is no polling loop.
        for(ushort key=1;key<255;key++)if((PInvoke.GetAsyncKeyState((int)key)&0x8000)!=0 && !IsMouseKey((VIRTUAL_KEY)key)
            && (VIRTUAL_KEY)key is not(VIRTUAL_KEY.VK_CONTROL or VIRTUAL_KEY.VK_SHIFT or VIRTUAL_KEY.VK_MENU))DownKeys.Add(key);
    }
    void ReleaseHooks()
    {
        if(!KeyboardHook.IsNull){PInvoke.UnhookWindowsHookEx(KeyboardHook);KeyboardHook=default;}
        if(!MouseHook.IsNull){PInvoke.UnhookWindowsHookEx(MouseHook);MouseHook=default;}
    }
    static bool IsMouseKey(VIRTUAL_KEY key)=>key is VIRTUAL_KEY.VK_LBUTTON or VIRTUAL_KEY.VK_RBUTTON or VIRTUAL_KEY.VK_MBUTTON or VIRTUAL_KEY.VK_XBUTTON1 or VIRTUAL_KEY.VK_XBUTTON2;
    static ShortcutModifiers Modifier(VIRTUAL_KEY key)=>key switch
    {
        VIRTUAL_KEY.VK_CONTROL or VIRTUAL_KEY.VK_LCONTROL or VIRTUAL_KEY.VK_RCONTROL=>ShortcutModifiers.Control,
        VIRTUAL_KEY.VK_SHIFT or VIRTUAL_KEY.VK_LSHIFT or VIRTUAL_KEY.VK_RSHIFT=>ShortcutModifiers.Shift,
        VIRTUAL_KEY.VK_MENU or VIRTUAL_KEY.VK_LMENU or VIRTUAL_KEY.VK_RMENU=>ShortcutModifiers.Alt,
        VIRTUAL_KEY.VK_LWIN or VIRTUAL_KEY.VK_RWIN=>ShortcutModifiers.Windows,_=>ShortcutModifiers.None
    };
    ShortcutChord Held()=>new(DownKeys.Aggregate(ShortcutModifiers.None,(value,key)=>value|Modifier((VIRTUAL_KEY)key)),
        DownKeys.Where(key=>Modifier((VIRTUAL_KEY)key)==ShortcutModifiers.None).Order().ToArray(),DownButtons.Order().ToArray());
    bool Evaluate(bool down)
    {
        var held=Held();
        if(IsCapturing)
        {
            if(down && held.IsValid){Candidate=held;Log($"Shortcut capture changed; chord={Display(held)}",Run:IsLogEnabled);Changed?.Invoke();}
            return false;
        }
        bool matches=held.Modifiers==Current.Modifiers && Current.Keys.All(DownKeys.Contains) && Current.Buttons.All(DownButtons.Contains)
            && held.Keys.Length==Current.Keys.Length && held.Buttons.Length==Current.Buttons.Length;
        bool activate=matches && !Matched;Matched=matches;
        if(activate){Log($"Shortcut pressed; chord={Display(Current)}",Run:IsLogEnabled);Toggle?.Invoke();}
        return matches || activate;
    }
        LRESULT Keyboard(int code,WPARAM message,LPARAM data)
    {
        try{return KeyboardCore(code,message,data);}
        catch(Exception error){ExceptionBoundary.LogSafely($"Shortcut Keyboard hook failed; error={error}",IsLogEnabled);return PInvoke.CallNextHookEx(default,code,message,data);}
    }
    unsafe LRESULT KeyboardCore(int code,WPARAM message,LPARAM data)
    {
        if(code>=0)
        {
            var key=((KBDLLHOOKSTRUCT*)data.Value)->vkCode;
            var flags=((KBDLLHOOKSTRUCT*)data.Value)->flags;
            if((flags & KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED)!=0)return PInvoke.CallNextHookEx(default,code,message,data);
            bool down=message.Value==PInvoke.WM_KEYDOWN || message.Value==PInvoke.WM_SYSKEYDOWN;
            bool up=message.Value==PInvoke.WM_KEYUP || message.Value==PInvoke.WM_SYSKEYUP;
            if(down||up)
            {
                bool changed=down?DownKeys.Add((ushort)key):DownKeys.Remove((ushort)key);
                bool handled=changed && Evaluate(down);
                if(IsCapturing || (handled && down) || (Matched && down && Current.Keys.Contains((ushort)key)))return new LRESULT(1);
            }
        }
        return PInvoke.CallNextHookEx(default,code,message,data);
    }
        LRESULT Mouse(int code,WPARAM message,LPARAM data)
    {
        try{return MouseCore(code,message,data);}
        catch(Exception error){ExceptionBoundary.LogSafely($"Shortcut Mouse hook failed; error={error}",IsLogEnabled);return PInvoke.CallNextHookEx(default,code,message,data);}
    }
    unsafe LRESULT MouseCore(int code,WPARAM message,LPARAM data)
    {
        if(code>=0)
        {
            var mouse=(MSLLHOOKSTRUCT*)data.Value;
            ShortcutMouse? button=(uint)message.Value switch
            {
                PInvoke.WM_LBUTTONDOWN or PInvoke.WM_LBUTTONUP=>ShortcutMouse.Left,
                PInvoke.WM_RBUTTONDOWN or PInvoke.WM_RBUTTONUP=>ShortcutMouse.Right,
                PInvoke.WM_MBUTTONDOWN or PInvoke.WM_MBUTTONUP=>ShortcutMouse.Middle,
                PInvoke.WM_XBUTTONDOWN or PInvoke.WM_XBUTTONUP=>(mouse->mouseData>>16)==PInvoke.XBUTTON1?ShortcutMouse.Mouse4:ShortcutMouse.Mouse5,_=>null
            };
            if(button is {} value)
            {
                bool down=message.Value==PInvoke.WM_LBUTTONDOWN || message.Value==PInvoke.WM_RBUTTONDOWN || message.Value==PInvoke.WM_MBUTTONDOWN || message.Value==PInvoke.WM_XBUTTONDOWN;
                bool bareLeft=value==ShortcutMouse.Left && Held().Modifiers==ShortcutModifiers.None && DownKeys.Count==0 && DownButtons.All(b=>b==ShortcutMouse.Left);
                if(IsCapturing && bareLeft)return PInvoke.CallNextHookEx(default,code,message,data);
                bool wasMatched=Matched;
                if(down)DownButtons.Add(value);else DownButtons.Remove(value);
                bool handled=Evaluate(down);
                if(IsCapturing || handled || wasMatched)return new LRESULT(1);
            }
        }
        return PInvoke.CallNextHookEx(default,code,message,data);
    }
    public Task BeginCaptureAsync()=>MainThread.InvokeOnMainThreadAsync(()=>
    {
        Candidate=null;IsCapturing=true;Log("Shortcut capture started",Run:IsLogEnabled);
        try{Configure();}catch{IsCapturing=false;Configure();throw;}
        Changed?.Invoke();
    });
    public Task EndCaptureAsync(bool accept)=>MainThread.InvokeOnMainThreadAsync(async ()=>
    {
        var previous=Current;var chosen=Candidate;
        IsCapturing=false;Candidate=null;
        if(accept && chosen is {IsValid:true})Current=chosen;
        try{Configure();}
        catch{Current=previous;Configure();Changed?.Invoke();throw;}
        Changed?.Invoke();
        Log($"Shortcut capture ended; accepted={accept}; chord={Display(Current)}",Run:IsLogEnabled);
        if(accept && chosen is {IsValid:true} && !await Preferences.SaveShortcutAsync(Current))throw new IOException("Shortcut preference could not be persisted.");
    });
    public string Display(ShortcutChord chord)
    {
        var names=new List<string>();
        if(chord.Modifiers.HasFlag(ShortcutModifiers.Control))names.Add("Ctrl");
        if(chord.Modifiers.HasFlag(ShortcutModifiers.Shift))names.Add("Shift");
        if(chord.Modifiers.HasFlag(ShortcutModifiers.Alt))names.Add("Alt");
        if(chord.Modifiers.HasFlag(ShortcutModifiers.Windows))names.Add("Win");
        names.AddRange(chord.Keys.Select(key=>(VIRTUAL_KEY)key==VIRTUAL_KEY.VK_OEM_102?"<":((Forms.Keys)key).ToString()));
        names.AddRange(chord.Buttons.Select(button=>button switch{ShortcutMouse.Mouse4=>"Mouse 4",ShortcutMouse.Mouse5=>"Mouse 5",_=>$"Mouse {button}"}));
        return string.Join(" + ",names);
    }
}


