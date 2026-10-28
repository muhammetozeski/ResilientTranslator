using Polly;
using static Logger;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using ResilientTranslator.MainProject.Infrastructure;
using ResilientTranslator.MainProject.Infrastructure.Localization;
using ResilientTranslator.MainProject.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Forms = System.Windows.Forms;
using NativeWindow = Microsoft.UI.Xaml.Window;
namespace ResilientTranslator;
/// <summary>Owns the warm window, global shortcut, tray actions and selection capture.</summary>
internal sealed class DesktopHost : Forms.NativeWindow, IDisposable
{
    const bool IsLogEnabled = true;
    const int HotkeyId = 1;
    enum DesktopAction { TrayOpen, TrayExit, TrayToggle, TrayMiddleExit, HideButton, FirstRender, Hotkey }
    static DesktopHost? Current;
    readonly NativeWindow Window;
    readonly Forms.NotifyIcon Tray;
    readonly Microsoft.UI.Dispatching.DispatcherQueue Dispatcher;
    readonly SemaphoreSlim ClipboardGate = new(1,1);
    RequestContext? CaptureRequest;
    Task? CaptureTask;

    bool Visible, Ready, ShowWhenReady, Disposed;
    /// <summary>Creates the tray receiver and prepares the window outside the visible desktop.</summary>
    DesktopHost(NativeWindow window)
    {
        Window = window; Dispatcher = window.DispatcherQueue;
        Microsoft.UI.Xaml.Application.Current.UnhandledException += (_, args) => ExceptionBoundary.LogSafely($"WinUI unhandled exception; message={args.Message}; error={args.Exception}");
        ShowWhenReady = Environment.GetCommandLineArgs().Contains("--show");
        window.AppWindow.IsShownInSwitchers = false;
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false;
        }
        window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(-30000,-30000,900,530));
        CreateHandle(new Forms.CreateParams { Caption = "ResilientTranslator.Hotkey" });
        if (!PInvoke.RegisterHotKey(new HWND(Handle), HotkeyId, HOT_KEY_MODIFIERS.MOD_WIN | HOT_KEY_MODIFIERS.MOD_NOREPEAT, (uint)VIRTUAL_KEY.VK_OEM_102)) throw new Win32Exception(Marshal.GetLastWin32Error());
        Tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
            Text = "Resilient Translator", Visible = true, ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        Tray.ContextMenuStrip.Items.Add(Strings.Open, null, (_,_) => Dispatch(Show, DesktopAction.TrayOpen));
        Tray.ContextMenuStrip.Items.Add(Strings.Exit, null, (_,_) => Dispatch(Exit, DesktopAction.TrayExit));
        Tray.MouseClick += (_,args) =>
        {
            Log($"Tray clicked; button={args.Button}; visible={Visible}", Run: IsLogEnabled);
            if (args.Button == Forms.MouseButtons.Left) Dispatch(Toggle, DesktopAction.TrayToggle);
            else if (args.Button == Forms.MouseButtons.Middle) Dispatch(Exit, DesktopAction.TrayMiddleExit);
        };
        window.AppWindow.Closing += (_,args) => { args.Cancel = true; Hide(); };
        TranslationWorkspace.RequestSuperseded += CancelCapture;
        TranslationWorkspace.HideWindow = () => Dispatch(Hide, DesktopAction.HideButton);
        TranslationWorkspace.ViewReady = () => Dispatch(OnReady, DesktopAction.FirstRender);
        Log("Desktop initialized; shortcut=Win+OEM102; trayLeft=toggle; trayMiddle=exit", Run: IsLogEnabled);
    }
    /// <summary>Attaches desktop behavior to the native MAUI window once.</summary>
    internal static void Attach(NativeWindow window) => Current ??= new DesktopHost(window);
    /// <summary>Executes each UI action immediately on the UI thread, or queues it once from other threads.</summary>
    void Dispatch(Action action, DesktopAction origin)
    {
        if (origin != DesktopAction.FirstRender) TranslationWorkspace.CancelPending();
        Log($"UI action received; origin={origin}; visible={Visible}; ready={Ready}", Run: IsLogEnabled);
        if (Dispatcher.HasThreadAccess) action();
        else if (!Dispatcher.TryEnqueue(() => action())) throw new InvalidOperationException($"UI dispatch rejected: {origin}");
    }
    /// <summary>Retains the completed visual tree, then hides or opens the preview.</summary>
    void OnReady() { if (Ready) return; Ready = true; Hide(); if (ShowWhenReady) Show(); Log("Blazor view ready; warm window retained", Run: IsLogEnabled); }
    /// <summary>Toggles visibility on every action independently of any pending capture.</summary>
    void Toggle() => ExceptionBoundary.RunSafely(() =>
    {
        if (Visible) { HideCore(); return; }
        ShowCore(activate: false);
        CaptureTask = CaptureAsync();
    }, operationName: "ToggleWindow");
    /// <summary>Opens the prepared window through the shared final error boundary.</summary>
    void Show() => ExceptionBoundary.RunSafely(() => ShowCore(activate: true), operationName: "ShowWindow");
    /// <summary>Shows the existing topmost window before any clipboard work starts.</summary>
    void ShowCore(bool activate)
    {
        if (!Ready) { ShowWhenReady = true; return; }
        Center();
        if (Window.AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = true;
        Window.AppWindow.Show(activate);
        if (activate) Window.Activate();
        Visible = true;
        var position = Window.AppWindow.Position; var size = Window.AppWindow.Size;
        Log($"Window shown; topmost=true; isVisible={Window.AppWindow.IsVisible}; activate={activate}; x={position.X}; y={position.Y}; width={size.Width}; height={size.Height}", Run: IsLogEnabled);
    }
    /// <summary>Contains errors from a hide action.</summary>
    void Hide() => ExceptionBoundary.RunSafely(HideCore, operationName: "HideWindow");
    /// <summary>Cancels pending capture and hides the window without destroying its visual tree.</summary>
    void HideCore()
    {
        CancelCapture();
        Window.AppWindow.Hide(); Visible = false;
        Log($"Window hidden; isVisible={Window.AppWindow.IsVisible}; pendingCaptureCanceled=true", Run: IsLogEnabled);
    }
    /// <summary>Centers the native window within the active work area.</summary>
    void Center()
    {
        var area = DisplayArea.GetFromWindowId(Window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = Window.AppWindow.Size;
        Window.AppWindow.Move(new Windows.Graphics.PointInt32(area.X+(area.Width-size.Width)/2,area.Y+(area.Height-size.Height)/2));
    }
    /// <summary>Handles the registered shortcut once per press through the existing Windows message loop.</summary>
    protected override void WndProc(ref Forms.Message message)
    {
        if (message.Msg == PInvoke.WM_HOTKEY && message.WParam == HotkeyId) Dispatch(Toggle, DesktopAction.Hotkey);
        base.WndProc(ref message);
    }
    /// <summary>Captures fresh selected text with event notifications and observes all asynchronous work.</summary>
    async Task CaptureAsync()
    {
        CancelCapture();
        RequestContext request = TranslationWorkspace.CurrentRequest;
        CaptureRequest = request;
        await ExceptionBoundary.RunSafelyAsync(async token =>
        {
            string? text = null;
            await ClipboardGate.WaitAsync(token).WaitAsync(token);
            try
            {
                Log($"Clipboard ownership acquired; request={request.Id}; thread={Environment.CurrentManagedThreadId}", Run: IsLogEnabled);
                var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler<EventArgs> handler = (_,_) => changed.TrySetResult();
                var clipboard = Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default;
                bool subscribed = false;
                try
                {
                    clipboard.ClipboardContentChanged += handler; subscribed = true;
                    Log("Capture waiting for modifier release event", Run: IsLogEnabled);
                    await ModifierReleaseWaiter.WaitAsync(token);
                    token.ThrowIfCancellationRequested();
                    uint sequence = PInvoke.GetClipboardSequenceNumber();
                    Log($"Ctrl+C starting; request={request.Id}; clipboardSequence={sequence}", Run: IsLogEnabled);
                    SendCopy();
                    try { await changed.Task.WaitAsync(TimeSpan.FromMilliseconds(450), token); }
                    catch (TimeoutException) { Log("Selection copy produced no clipboard change", Run: IsLogEnabled); return; }
                    uint newSequence = PInvoke.GetClipboardSequenceNumber();
                    Log($"Clipboard event; previousSequence={sequence}; newSequence={newSequence}; hasText={clipboard.HasText}", Run: IsLogEnabled);
                    if (newSequence == sequence || !clipboard.HasText) return;
                    text = await ReadClipboardTextAsync(token);
                    uint afterReadSequence = PInvoke.GetClipboardSequenceNumber();
                    if (request != CaptureRequest || token.IsCancellationRequested || afterReadSequence != newSequence)
                    {
                        Log($"Clipboard result discarded; request={request.Id}; currentRequest={CaptureRequest?.Id}; beforeReadSequence={newSequence}; afterReadSequence={afterReadSequence}", Run: IsLogEnabled);
                        token.ThrowIfCancellationRequested();
                        return;
                    }
                    Log($"Clipboard read; length={text?.Length ?? 0}; text={text}", Run: IsLogEnabled);
                }
                finally { if (subscribed) clipboard.ClipboardContentChanged -= handler; }
            }
            finally
            {
                ClipboardGate.Release();
                Log($"Clipboard ownership released; request={request.Id}", Run: IsLogEnabled);
                if (Visible && !token.IsCancellationRequested && CaptureRequest == request)
                {
                    Window.Activate();
                    Log($"Window activated after capture; isVisible={Window.AppWindow.IsVisible}; topmost=true", Run: IsLogEnabled);
                }
            }
            token.ThrowIfCancellationRequested();
            if (request == CaptureRequest && !string.IsNullOrWhiteSpace(text))
                await TranslationWorkspace.EditAsync(text,true,true,expectedRequest:request).WaitAsync(token);
        }, request.Token, operationName: "CaptureSelection");
        if (CaptureRequest == request) CaptureRequest = null;
    }
    /// <summary>Invalidates a pending capture when a newer editor or desktop command arrives.</summary>
    void CancelCapture()
    {
        Volatile.Read(ref CaptureRequest)?.Cancel();
    }
    /// <summary>Retries transient clipboard access, then reads through an independent STA fallback without blocking the UI.</summary>
    async Task<string?> ReadClipboardTextAsync(CancellationToken token)
    {
        try
        {
            return await Resilience.ExecuteResilientlyAsync(
                ct => ReadOnUiThreadAsync(() => Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.GetTextAsync(), ct),
                ClipboardReadCanRetry, token, retryCount: 2, attemptTimeout: TimeSpan.FromMilliseconds(250), totalTimeout: TimeSpan.FromMilliseconds(850), retryDelay: TimeSpan.FromMilliseconds(15), operationName: "ReadMauiClipboard");
        }
        catch (Exception primaryError) when (primaryError is not OperationCanceledException)
        {
            Log($"MAUI clipboard read failed; trying STA clipboard reader; error={primaryError}", Run: IsLogEnabled);
            try
            {
                return await Resilience.ExecuteResilientlyAsync(ReadClipboardOnStaThreadAsync,
                    ClipboardReadCanRetry, token, retryCount: 1, attemptTimeout: TimeSpan.FromSeconds(2), totalTimeout: TimeSpan.FromSeconds(3), retryDelay: TimeSpan.FromMilliseconds(15), operationName: "ReadStaClipboard");
            }
            catch (Exception fallbackError) when (fallbackError is not OperationCanceledException)
            {
                throw new AggregateException("Both clipboard readers failed.", primaryError, fallbackError);
            }
        }
    }

    /// <summary>Selects only busy clipboard access and bounded read timeouts for replay-safe retries.</summary>
    static bool ClipboardReadCanRetry(Exception error) =>
        error is Polly.Timeout.TimeoutRejectedException ||
        error is ExternalException external && external.ErrorCode == HRESULT.CLIPBRD_E_CANT_OPEN.Value;

    /// <summary>Starts the MAUI clipboard read on its owning dispatcher and rejects a canceled completion.</summary>
    Task<string?> ReadOnUiThreadAsync(Func<Task<string?>> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Dispatcher.HasThreadAccess) return operation().WaitAsync(token);
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Dispatcher.TryEnqueue(async () =>
        {
            try { token.ThrowIfCancellationRequested(); string? value = await operation().WaitAsync(token); token.ThrowIfCancellationRequested(); completion.TrySetResult(value); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { completion.TrySetCanceled(token); }
            catch (Exception error) { completion.TrySetException(error); }
        })) throw new InvalidOperationException("Clipboard dispatcher is unavailable.");
        return completion.Task.WaitAsync(token);
    }

    /// <summary>Uses a private STA thread for the synchronous OLE clipboard API; it returns data without changing shared state.</summary>
    static Task<string?> ReadClipboardOnStaThreadAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try { token.ThrowIfCancellationRequested(); string text = Forms.Clipboard.GetText(Forms.TextDataFormat.UnicodeText); token.ThrowIfCancellationRequested(); completion.TrySetResult(text); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { completion.TrySetCanceled(token); }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true, Name = "ClipboardFallback" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task.WaitAsync(token);
    }
    /// <summary>Sends one Ctrl+C chord with generated Windows input declarations.</summary>
    static unsafe void SendCopy()
    {
        INPUT[] inputs = [new(){type=INPUT_TYPE.INPUT_KEYBOARD,Anonymous=new(){ki=new(){wVk=VIRTUAL_KEY.VK_CONTROL}}},new(){type=INPUT_TYPE.INPUT_KEYBOARD,Anonymous=new(){ki=new(){wVk=VIRTUAL_KEY.VK_C}}},new(){type=INPUT_TYPE.INPUT_KEYBOARD,Anonymous=new(){ki=new(){wVk=VIRTUAL_KEY.VK_C,dwFlags=KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP}}},new(){type=INPUT_TYPE.INPUT_KEYBOARD,Anonymous=new(){ki=new(){wVk=VIRTUAL_KEY.VK_CONTROL,dwFlags=KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP}}}];
        if (PInvoke.SendInput(inputs,sizeof(INPUT)) != inputs.Length) throw new Win32Exception(Marshal.GetLastWin32Error());
        Log($"Ctrl+C sent; inputCount={inputs.Length}", Run: IsLogEnabled);
    }
    /// <summary>Exits the application for a middle click or the explicit tray command.</summary>
    void Exit() => ExceptionBoundary.RunSafely(() => { Log("Application exit requested", Run: IsLogEnabled, WaitForLogging: true); Dispose(); Microsoft.Maui.Controls.Application.Current?.Quit(); }, operationName: "ExitApplication");
    /// <summary>Releases the temporary capture, global shortcut and tray resources once.</summary>
    public void Dispose()
    {
        if (Disposed) return; Disposed = true;
        TranslationWorkspace.RequestSuperseded -= CancelCapture;
        CancelCapture();
        PInvoke.UnregisterHotKey(new HWND(Handle),HotkeyId);
        Tray.Visible=false; Tray.Dispose(); DestroyHandle();
        Log("Desktop disposed", Run:IsLogEnabled);
    }
}








