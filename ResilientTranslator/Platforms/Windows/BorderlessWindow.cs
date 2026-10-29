using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using NativeWindow = Microsoft.UI.Xaml.Window;

namespace MauiWindowing;

/// <summary>Optional limits and callbacks for an HTML-owned MAUI Windows surface.</summary>
public sealed class BorderlessWindowOptions
{
    public int MinimumWidth = 480;
    public int MinimumHeight = 280;
    public bool AlwaysOnTop = true;
    public Action<string>? Log;
    public Action<string, string>? Command;
    public Action<int, int>? ResizeCompleted;
}

/// <summary>
/// Removes all Windows nonclient insets and installs background-only left dragging,
/// universal middle dragging and an HTML resize grip. Copy this file into a Windows MAUI project.
/// Requires Microsoft.Windows.CsWin32 and the NativeMethods names documented in Docs/Window-Interaction.md.
/// Call the constructor from OnWindowCreated and AttachBrowserAsync from BlazorWebViewInitialized.
/// HTML marks only actual background elements with data-window-background and its grip with data-window-resize.
/// </summary>
public sealed class BorderlessWindow : IDisposable
{
    const nuint SubclassId = 1;
    const WINDOW_STYLE FrameStyles = WINDOW_STYLE.WS_CAPTION | WINDOW_STYLE.WS_THICKFRAME | WINDOW_STYLE.WS_BORDER | WINDOW_STYLE.WS_DLGFRAME | WINDOW_STYLE.WS_SYSMENU | WINDOW_STYLE.WS_MINIMIZEBOX | WINDOW_STYLE.WS_MAXIMIZEBOX;
    const WINDOW_EX_STYLE EdgeStyles = WINDOW_EX_STYLE.WS_EX_WINDOWEDGE | WINDOW_EX_STYLE.WS_EX_CLIENTEDGE | WINDOW_EX_STYLE.WS_EX_DLGMODALFRAME | WINDOW_EX_STYLE.WS_EX_STATICEDGE;
    readonly NativeWindow Window;
    readonly HWND Handle;
    readonly BorderlessWindowOptions Options;
    readonly SUBCLASSPROC Procedure;
    CoreWebView2? Browser;
    Gesture? Interaction;
    bool Disposed;

    /// <summary>Installs full-client frame handling and refreshes the native window rectangle.</summary>
    /// <param name="window">The MAUI application's native Windows window.</param>
    /// <param name="options">Optional sizing policy and application callbacks.</param>
    public unsafe BorderlessWindow(NativeWindow window, BorderlessWindowOptions? options = null)
    {
        Window = window;
        Options = options ?? new();
        Handle = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));
        Procedure = WindowProcedure;
        window.SystemBackdrop = null;
        window.ExtendsContentIntoTitleBar = true;
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        window.SetTitleBar(null);
        if (!PInvoke.SetWindowSubclass(Handle, Procedure, SubclassId, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = Options.AlwaysOnTop;
        }
        nint style = PInvoke.GetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        PInvoke.SetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE, (nint)(((WINDOW_STYLE)(uint)style & ~FrameStyles) | WINDOW_STYLE.WS_POPUP));
        nint extended = PInvoke.GetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, extended & ~(nint)EdgeStyles);
        DWMNCRENDERINGPOLICY rendering = DWMNCRENDERINGPOLICY.DWMNCRP_DISABLED;
        PInvoke.DwmSetWindowAttribute(Handle, DWMWINDOWATTRIBUTE.DWMWA_NCRENDERING_POLICY, &rendering, (uint)sizeof(DWMNCRENDERINGPOLICY)).ThrowOnFailure();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            DWM_WINDOW_CORNER_PREFERENCE corners = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
            PInvoke.DwmSetWindowAttribute(Handle, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corners, (uint)sizeof(DWM_WINDOW_CORNER_PREFERENCE)).ThrowOnFailure();
        }
        if (!PInvoke.SetWindowPos(Handle, default, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE)) throw new Win32Exception();
        window.Activated += OnActivated;
        Options.Log?.Invoke("Borderless full-client window configured");
    }

    /// <summary>Installs capture-phase input before application handlers and keeps native commands independent of Blazor events.</summary>
    /// <param name="browser">The initialized browser hosting the MAUI Blazor page.</param>
    public async Task AttachBrowserAsync(CoreWebView2 browser)
    {
        if (Browser is not null) Browser.WebMessageReceived -= OnMessage;
        Browser = browser;
        browser.WebMessageReceived += OnMessage;
        await browser.AddScriptToExecuteOnDocumentCreatedAsync(InputScript);
        await browser.ExecuteScriptAsync(InputScript);
    }

    /// <summary>Suppresses retained nonclient insets and prevents WinUI from restoring native caption styles.</summary>
    unsafe LRESULT WindowProcedure(HWND window, uint message, WPARAM parameter, LPARAM data, nuint subclass, nuint reference)
    {
        if (message == PInvoke.WM_NCCALCSIZE) return default;
        if (message == PInvoke.WM_STYLECHANGING && (nint)parameter.Value == (nint)WINDOW_LONG_PTR_INDEX.GWL_STYLE)
        {
            STYLESTRUCT* change = (STYLESTRUCT*)data.Value;
            change->styleNew = (uint)(((WINDOW_STYLE)change->styleNew & ~FrameStyles) | WINDOW_STYLE.WS_POPUP);
        }
        return PInvoke.DefSubclassProc(window, message, parameter, data);
    }

    /// <summary>Ends ownership whenever the native window deactivates.</summary>
    void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated) Interaction = null;
    }

    /// <summary>Consumes only this surface's messages; catches errors before they cross the WebView event boundary.</summary>
    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            if (Disposed) return;
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("mauiWindow", out var type)) return;
            switch (type.GetString())
            {
                case "begin":
                    if (!PInvoke.GetCursorPos(out var cursor)) throw new Win32Exception();
                    Interaction = new(cursor.X, cursor.Y, Window.AppWindow.Position, Window.AppWindow.Size, root.GetProperty("resize").GetBoolean());
                    Options.Log?.Invoke($"Window gesture started; resize={Interaction.Resize}");
                    break;
                case "move":
                    if (Interaction is not { } gesture) return;
                    if (!PInvoke.GetCursorPos(out cursor)) throw new Win32Exception();
                    int deltaX = cursor.X - gesture.X, deltaY = cursor.Y - gesture.Y;
                    double scale = PInvoke.GetDpiForWindow(Handle) / (double)PInvoke.USER_DEFAULT_SCREEN_DPI;
                    if (gesture.Resize)
                        Window.AppWindow.Resize(new SizeInt32(Math.Max((int)(Options.MinimumWidth * scale), gesture.Size.Width + deltaX), Math.Max((int)(Options.MinimumHeight * scale), gesture.Size.Height + deltaY)));
                    else Window.AppWindow.Move(new PointInt32(gesture.Position.X + deltaX, gesture.Position.Y + deltaY));
                    break;
                case "end":
                    var completed = Interaction;
                    Interaction = null;
                    if (completed?.Resize == true) Options.ResizeCompleted?.Invoke(Window.AppWindow.Size.Width, Window.AppWindow.Size.Height);
                    Options.Log?.Invoke("Window gesture ended");
                    break;
                case "command":
                    Options.Command?.Invoke(root.GetProperty("command").GetString() ?? "", root.GetProperty("text").GetString() ?? "");
                    break;
            }
        }
        catch (Exception error) { Interaction = null; Options.Log?.Invoke($"Native window input failed; error={error}"); }
    }

    /// <summary>Detaches native and browser handlers; no gesture survives disposal.</summary>
    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        Interaction = null;
        if (Browser is not null) Browser.WebMessageReceived -= OnMessage;
        Window.Activated -= OnActivated;
        PInvoke.RemoveWindowSubclass(Handle, Procedure, SubclassId);
        GC.KeepAlive(Procedure);
    }

    sealed record Gesture(int X, int Y, PointInt32 Position, SizeInt32 Size, bool Resize);

    const string InputScript = """
        (() => {
            if (window.mauiWindowInputInstalled) return;
            window.mauiWindowInputInstalled = true;
            let gesture = null;
            const send = message => window.chrome.webview.postMessage(message);
            const suppress = event => { event.preventDefault(); event.stopImmediatePropagation(); };
            const finish = () => {
                const previous = gesture;
                if (!previous) return;
                gesture = null;
                if (previous.frame) cancelAnimationFrame(previous.frame);
                if (document.documentElement.hasPointerCapture(previous.pointer)) document.documentElement.releasePointerCapture(previous.pointer);
                send({mauiWindow:'end'});
            };
            window.addEventListener('pointerdown', event => {
                if (!(event.target instanceof Element)) return;
                const middle = event.button === 1;
                const resize = event.button === 0 && event.target.closest('[data-window-resize]');
                const background = event.button === 0 && event.target.matches('[data-window-background]');
                if (!middle && !resize && !background) return;
                suppress(event);
                finish();
                gesture = {pointer:event.pointerId, button:middle ? 4 : 1, frame:0};
                document.documentElement.setPointerCapture(event.pointerId);
                send({mauiWindow:'begin',resize:!!resize && !middle});
            }, true);
            window.addEventListener('pointermove', event => {
                const active = gesture;
                if (!active || active.pointer !== event.pointerId) return;
                suppress(event);
                if (!(event.buttons & active.button)) { finish(); return; }
                if (active.frame) return;
                active.frame = requestAnimationFrame(() => {
                    active.frame = 0;
                    if (gesture === active) send({mauiWindow:'move'});
                });
            }, true);
            window.addEventListener('pointerup', event => { if (gesture) { suppress(event); finish(); } }, true);
            for (const name of ['pointercancel','lostpointercapture','blur']) window.addEventListener(name, finish, true);
            window.addEventListener('auxclick', event => { if (event.button === 1) suppress(event); }, true);
            for (const name of ['mousedown','mouseup']) window.addEventListener(name, event => { if (event.button === 1) suppress(event); }, true);
            window.addEventListener('click', event => {
                const command = event.target instanceof Element && event.target.closest('[data-native-command]');
                if (!command) return;
                suppress(event);
                send({mauiWindow:'command',command:command.dataset.nativeCommand,text:document.getElementById('input')?.value ?? ''});
            }, true);
        })();
        """;
}
