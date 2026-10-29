using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;
using Microsoft.UI.Xaml;
using Polly;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using ResilientTranslator.MainProject.Infrastructure;
using ResilientTranslator.MainProject.Infrastructure.Localization;
using ResilientTranslator.MainProject.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Preferences = ResilientTranslator.MainProject.Infrastructure.Preferences;
using ResilientTranslator.MainProject.Constants;
using Forms = System.Windows.Forms;
using NativeWindow = Microsoft.UI.Xaml.Window;

namespace ResilientTranslator;
/// <summary>Owns the warm window, global shortcut, tray actions and selection capture.</summary>
internal sealed class DesktopHost : Forms.NativeWindow, IDisposable
{
    const bool IsLogEnabled = true;
    const int HotkeyId = 1;
    /// <summary>Distinguishes native command origins without using display strings as request ownership.</summary>
    enum DesktopAction
    {
        TrayOpen,
        TrayExit,
        TrayToggle,
        TrayMiddleExit,
        HideButton,
        FirstRender,
        Hotkey,
        OutsideClick
    }

    static DesktopHost? Current;
    readonly NativeWindow Window;
    readonly MauiWindowing.BorderlessWindow Chrome;
    bool Positioned;
    readonly HWND MainHandle;
    readonly WINEVENTPROC ForegroundCallback;
    HWINEVENTHOOK ForegroundHook;
    HWND SourceForeground;
    RequestContext? Presentation;
    readonly Forms.NotifyIcon Tray;
    readonly Microsoft.UI.Dispatching.DispatcherQueue Dispatcher;
    readonly SemaphoreSlim ClipboardGate = new(1, 1);
    RequestContext? CaptureRequest;
    Task? CaptureTask;
    Task? FocusTask;
    Task? RecoveryTask;
    Task? ExitTask;
    bool Visible, Ready, ShowWhenReady, Disposed, ActivationObserved;
    /// <summary>Creates the tray receiver and prepares the window outside the visible desktop.</summary>
    /// <param name = "window">The native window whose client area or foreground ownership is being handled.</param>
    DesktopHost(NativeWindow window)
    {
        Window = window;
        Dispatcher = window.DispatcherQueue;
        MainHandle = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));
        ForegroundCallback = OnForegroundChanged;
        try
        {
            ForegroundHook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND, default, ForegroundCallback, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
            if (ForegroundHook.IsNull)
                Log($"Foreground event hook unavailable; retaining WinUI activation fallback; error={Marshal.GetLastWin32Error()}", Run: IsLogEnabled);
            Microsoft.UI.Xaml.Application.Current.UnhandledException += OnUnhandledException;
            ShowWhenReady = Environment.GetCommandLineArgs().Contains("--show");
            window.AppWindow.IsShownInSwitchers = false;
            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }

            Chrome = new(window, new() { Log = message => ExceptionBoundary.LogSafely(message), Command = OnNativeCommand, ResizeCompleted = (width, height) => _ = Preferences.SaveWindowSizeAsync(width, height) });
            window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(-30000, -30000, Preferences.WindowWidth, Preferences.WindowHeight));
            Preferences.LoadFonts = FontCatalog.LoadAsync;
            CreateHandle(new Forms.CreateParams { Caption = "ResilientTranslator.Hotkey" });
            if (!PInvoke.RegisterHotKey(new HWND(Handle), HotkeyId, HOT_KEY_MODIFIERS.MOD_WIN | HOT_KEY_MODIFIERS.MOD_NOREPEAT, (uint)VIRTUAL_KEY.VK_OEM_102))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Tray = new Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
                Text = AppConstants.AppNameHumanReadable,
                Visible = true,
                ContextMenuStrip = new Forms.ContextMenuStrip()
            };
            Tray.ContextMenuStrip.Items.Add(Strings.Open, null, (_, _) => Dispatch(Show, DesktopAction.TrayOpen));
            Tray.ContextMenuStrip.Items.Add(Strings.Exit, null, (_, _) => Dispatch(Exit, DesktopAction.TrayExit));
            Tray.MouseClick += (_, args) =>
            {
                Log($"Tray clicked; button={args.Button}; visible={Visible}", Run: IsLogEnabled);
                if (args.Button == Forms.MouseButtons.Left)
                    Dispatch(Toggle, DesktopAction.TrayToggle);
                else if (args.Button == Forms.MouseButtons.Middle)
                    Dispatch(Exit, DesktopAction.TrayMiddleExit);
            };
            window.Activated += OnActivationChanged;
            LocManager.LanguageChanged += RefreshTrayLanguage;
            window.AppWindow.Closing += OnClosing;
            TranslationWorkspace.RequestSuperseded += CancelCapture;
            TranslationWorkspace.HideWindow = () => Dispatch(Hide, DesktopAction.HideButton);
            TranslationWorkspace.ViewReady = () => Dispatch(OnReady, DesktopAction.FirstRender);
            Log("Desktop initialized; shortcut=Win+OEM102; trayLeft=toggle; trayMiddle=exit", Run: IsLogEnabled);
        }
        catch (Exception initializationError)
        {
            try
            {
                Dispose();
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException("Desktop initialization and cleanup failed.", initializationError, cleanupError);
            }

            throw;
        }
    }

    /// <summary>Contains recoverable managed UI failures while retaining fatal runtime failures for process termination.</summary>
    /// <param name = "sender">The framework object delivering this callback.</param>
    /// <param name = "args">Framework callback arguments, or the original process arguments for an entry point.</param>
    void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        ExceptionBoundary.LogSafely($"WinUI unhandled exception; message={args.Message}; error={args.Exception}", IsLogEnabled);
        if (args.Exception is OutOfMemoryException or AccessViolationException or StackOverflowException)
            return;
        args.Handled = true;
        RecoveryTask = RecoverViewAsync();
    }

    /// <summary>Retains the resident window instead of closing the translator process.</summary>
    /// <param name = "sender">The framework object delivering this callback.</param>
    /// <param name = "args">Framework callback arguments, or the original process arguments for an entry point.</param>
    void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        if (!Disposed)
            Hide();
    }

    /// <summary>Connects native browser input independently of Blazor UI event dispatch.</summary>
    /// <param name = "browser">The initialized WebView2 instance hosting the shared UI.</param>
    /// <returns>Completion of browser input registration and script installation.</returns>
    internal static Task AttachBrowserAsync(Microsoft.Web.WebView2.Core.CoreWebView2 browser) => Current?.Chrome.AttachBrowserAsync(browser) ?? Task.CompletedTask;
    /// <summary>Starts a fresh manual request or hides immediately, regardless of pending editor work.</summary>
    /// <param name = "command">The native command selected by the actual HTML control.</param>
    /// <param name = "text">The complete immutable text accepted by this operation.</param>
    void OnNativeCommand(string command, string? text)
    {
        if (command == "hide")
            Dispatch(Hide, DesktopAction.HideButton);
        else if (command == "recover")
            RecoveryTask = RecoverViewAsync();
        else if (command == "translate")
        {
            string snapshot = text ?? TranslationWorkspace.Input;
            TranslationWorkspace.CancelPending();
            var owner = TranslationWorkspace.CurrentRequest;
            RecoveryTask = RecoverViewAsync();
            _ = ExceptionBoundary.RunSafelyAsync(async token =>
            {
                await RecoverViewAsync().WaitAsync(token);
                Log("Manual translation requested; bypassCache=true; debounce=false", Run: IsLogEnabled);
                await TranslationWorkspace.EditAsync(snapshot, true, true, expectedRequest: owner, forceNetwork: true).WaitAsync(token);
            }, owner.Token, operationName: "ManualTranslation");
        }
    }

    /// <summary>Requests UI recovery independently of the backend command; a failed view cannot suppress translation.</summary>
    /// <returns>Completion of the contained view-recovery request.</returns>
    static Task RecoverViewAsync() => ExceptionBoundary.RunSafelyAsync(async token =>
    {
        if (TranslationWorkspace.RecoverView is { } recover)
            await recover().WaitAsync(token);
    }, operationName: "NativeViewRecovery");
    /// <summary>Attaches desktop behavior to the native MAUI window once.</summary>
    /// <param name = "window">The native window whose client area or foreground ownership is being handled.</param>
    internal static void Attach(NativeWindow window) => Current ??= new DesktopHost(window);
    /// <summary>Executes each UI action immediately on the UI thread, or queues it once from other threads.</summary>
    /// <param name = "action">The native UI action to execute on the owning dispatcher.</param>
    /// <param name = "origin">The typed command origin recorded with this native UI action.</param>
    void Dispatch(Action action, DesktopAction origin)
    {
        if (Disposed)
            return;
        if (origin != DesktopAction.FirstRender)
            TranslationWorkspace.CancelPending();
        Log($"UI action received; origin={origin}; visible={Visible}; ready={Ready}", Run: IsLogEnabled);
        if (Dispatcher.HasThreadAccess)
            action();
        else if (!Dispatcher.TryEnqueue(() => action()))
            throw new InvalidOperationException($"UI dispatch rejected: {origin}");
    }

    /// <summary>Retains the completed visual tree, then hides or opens the preview.</summary>
    void OnReady()
    {
        if (Ready)
            return;
        Ready = true;
        Hide();
        if (ShowWhenReady)
            Show();
        Log("Blazor view ready; warm window retained", Run: IsLogEnabled);
    }

    /// <summary>Toggles visibility on every action independently of any pending capture.</summary>
    void Toggle() => ExceptionBoundary.RunSafely(() =>
    {
        if (Visible)
        {
            HideCore();
            return;
        }

        ShowCore(activate: false);
        CaptureTask = CaptureAsync();
    }, operationName: "ToggleWindow");
    /// <summary>Opens the prepared window through the shared final error boundary.</summary>
    void Show() => ExceptionBoundary.RunSafely(() => ShowCore(activate: true), operationName: "ShowWindow");
    /// <summary>Shows the existing topmost window before any clipboard work starts.</summary>
    /// <param name = "activate">Whether showing this presentation should also acquire keyboard focus.</param>
    void ShowCore(bool activate)
    {
        if (!Ready)
        {
            ShowWhenReady = true;
            return;
        }

        if (!Positioned)
        {
            Center();
            Positioned = true;
        }

        if (Window.AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = true;
        Presentation?.Cancel();
        Presentation = new();
        SourceForeground = RootOwner(PInvoke.GetForegroundWindow());
        ActivationObserved = false;
        Visible = true;
        Window.AppWindow.Show(activate);
        if (activate)
            ActivateWindow();
        var position = Window.AppWindow.Position;
        var size = Window.AppWindow.Size;
        Log($"Window shown; topmost=true; isVisible={Window.AppWindow.IsVisible}; activate={activate}; x={position.X}; y={position.Y}; width={size.Width}; height={size.Height}", Run: IsLogEnabled);
    }

    /// <summary>Activates the native window and observes the asynchronous focus request inside its own error boundary.</summary>
    void ActivateWindow()
    {
        if (Presentation is not { } owner || !Visible)
            return;
        FocusTask = ExceptionBoundary.RunSafelyAsync(async token =>
        {
            token.ThrowIfCancellationRequested();
            Window.AppWindow.Show(true);
            Window.Activate();
            if (RootOwner(PInvoke.GetForegroundWindow()) != MainHandle)
            {
                bool accepted = PInvoke.SetForegroundWindow(MainHandle);
                Log($"Native foreground request; accepted={accepted}", Run: IsLogEnabled);
            }

            if (TranslationWorkspace.FocusWebView is { } host)
                await host(token).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            await RecoverViewAsync().WaitAsync(token);
            if (TranslationWorkspace.FocusInput is { } focus)
                await focus(token).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            bool foreground = RootOwner(PInvoke.GetForegroundWindow()) == MainHandle;
            ActivationObserved = foreground;
            Log($"Input focus completed; realForeground={foreground}; visible={Visible}", Run: IsLogEnabled);
            if (!foreground)
                throw new InvalidOperationException("The translator did not acquire the foreground window.");
        }, owner.Token, operationName: "FocusInput");
    }

    /// <summary>Uses native foreground ownership rather than logical activation alone.</summary>
    /// <param name = "sender">The framework object delivering this callback.</param>
    /// <param name = "args">Framework callback arguments, or the original process arguments for an entry point.</param>
    void OnActivationChanged(object sender, WindowActivatedEventArgs args)
    {
        bool actual = RootOwner(PInvoke.GetForegroundWindow()) == MainHandle;
        Log($"Window activation changed; state={args.WindowActivationState}; actualForeground={actual}; visible={Visible}", Run: IsLogEnabled);
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            if (Visible && actual)
                ActivationObserved = true;
            return;
        }

        if (!Disposed && Ready && Visible && ActivationObserved && !actual)
            Dispatch(Hide, DesktopAction.OutsideClick);
    }

    /// <summary>Receives foreground-change events without polling and ignores child or owned browser windows.</summary>
    /// <param name = "hook">The registered accessibility hook receiving this event.</param>
    /// <param name = "eventType">The native accessibility event kind.</param>
    /// <param name = "window">The native window whose client area or foreground ownership is being handled.</param>
    /// <param name = "objectId">The object identifier supplied by Windows accessibility.</param>
    /// <param name = "childId">The child identifier supplied by the native accessibility notification.</param>
    /// <param name = "thread">The native event&apos;s originating thread identifier.</param>
    /// <param name = "time">The native event timestamp supplied by Windows.</param>
    void OnForegroundChanged(HWINEVENTHOOK hook, uint eventType, HWND window, int objectId, int childId, uint thread, uint time)
    {
        try
        {
            if (Disposed || !Ready || !Visible || window.IsNull || Presentation is not { } owner)
                return;
            HWND root = RootOwner(window);
            HWND actual = RootOwner(PInvoke.GetForegroundWindow());
            if (root != actual)
            {
                Log("Stale foreground notification ignored", Run: IsLogEnabled);
                return;
            }

            if (root == MainHandle)
            {
                ActivationObserved = true;
                return;
            }

            if (!ActivationObserved && root == SourceForeground)
                return;
            Log("Outside foreground event received", Run: IsLogEnabled);
            Dispatcher.TryEnqueue(() =>
            {
                if (Visible && Presentation == owner && RootOwner(PInvoke.GetForegroundWindow()) != MainHandle)
                    Dispatch(Hide, DesktopAction.OutsideClick);
            });
        }
        catch (Exception error)
        {
            ExceptionBoundary.LogSafely($"Foreground event handling failed; error={error}");
        }
    }

    /// <summary>Finds the owning top-level window for an active browser child or popup.</summary>
    /// <param name = "window">The native window whose client area or foreground ownership is being handled.</param>
    /// <returns>The owning top-level window, or the supplied handle when no owner exists.</returns>
    static HWND RootOwner(HWND window)
    {
        HWND root = PInvoke.GetAncestor(window, GET_ANCESTOR_FLAGS.GA_ROOTOWNER);
        return root.IsNull ? window : root;
    }

    /// <summary>Contains errors from a hide action.</summary>
    void Hide() => ExceptionBoundary.RunSafely(HideCore, operationName: "HideWindow");
    /// <summary>Cancels pending capture and hides the window without destroying its visual tree.</summary>
    void HideCore()
    {
        CancelCapture();
        Presentation?.Cancel();
        bool wasVisible = Visible, wasActivated = ActivationObserved;
        Visible = false;
        ActivationObserved = false;
        try
        {
            Window.AppWindow.Hide();
        }
        catch
        {
            Visible = wasVisible;
            ActivationObserved = wasActivated;
            throw;
        }

        TranslationWorkspace.NotifyWindowHidden();
        Log($"Window hidden; isVisible={Window.AppWindow.IsVisible}; pendingCaptureCanceled=true", Run: IsLogEnabled);
    }

    /// <summary>Updates tray labels on the native dispatcher after a UI-language selection.</summary>
    void RefreshTrayLanguage() => Dispatcher.TryEnqueue(() =>
    {
        if (Disposed || Tray.ContextMenuStrip is not { } menu || menu.Items.Count < 2)
            return;
        menu.Items[0].Text = Strings.Open;
        menu.Items[1].Text = Strings.Exit;
    });
    /// <summary>Centers the native window within the active work area.</summary>
    void Center()
    {
        var area = DisplayArea.GetFromWindowId(Window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = Window.AppWindow.Size;
        Window.AppWindow.Move(new Windows.Graphics.PointInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2));
    }

    /// <summary>Handles the registered shortcut once per press through the existing Windows message loop.</summary>
    /// <param name = "message">The complete diagnostic text or native Windows message identifier.</param>
    protected override void WndProc(ref Forms.Message message)
    {
        if (message.Msg == PInvoke.WM_HOTKEY && message.WParam == HotkeyId)
            Dispatch(Toggle, DesktopAction.Hotkey);
        base.WndProc(ref message);
    }

    /// <summary>Captures fresh selected text with event notifications and observes all asynchronous work.</summary>
    /// <returns>Completion or cancellation of this fresh-selection capture and its accepted translation.</returns>
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
                EventHandler<EventArgs> handler = (_, _) => changed.TrySetResult();
                var clipboard = Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default;
                bool subscribed = false;
                try
                {
                    clipboard.ClipboardContentChanged += handler;
                    subscribed = true;
                    Log("Capture waiting for modifier release event", Run: IsLogEnabled);
                    await ModifierReleaseWaiter.WaitAsync(token);
                    token.ThrowIfCancellationRequested();
                    uint sequence = PInvoke.GetClipboardSequenceNumber();
                    Log($"Ctrl+C starting; request={request.Id}; clipboardSequence={sequence}", Run: IsLogEnabled);
                    SendCopy();
                    try
                    {
                        await changed.Task.WaitAsync(TimeSpan.FromMilliseconds(Preferences.ClipboardChangeBudget), token);
                    }
                    catch (TimeoutException)
                    {
                        Log("Selection copy produced no clipboard change", Run: IsLogEnabled);
                        return;
                    }

                    uint newSequence = PInvoke.GetClipboardSequenceNumber();
                    Log($"Clipboard event; previousSequence={sequence}; newSequence={newSequence}; hasText={clipboard.HasText}", Run: IsLogEnabled);
                    if (newSequence == sequence || !clipboard.HasText)
                        return;
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
                finally
                {
                    if (subscribed)
                        clipboard.ClipboardContentChanged -= handler;
                }
            }
            finally
            {
                ClipboardGate.Release();
                Log($"Clipboard ownership released; request={request.Id}", Run: IsLogEnabled);
                if (Visible && !token.IsCancellationRequested && CaptureRequest == request)
                {
                    ActivateWindow();
                    Log($"Window activated after capture; isVisible={Window.AppWindow.IsVisible}; topmost=true", Run: IsLogEnabled);
                }
            }

            token.ThrowIfCancellationRequested();
            if (request == CaptureRequest && !string.IsNullOrWhiteSpace(text))
                await TranslationWorkspace.EditAsync(text, true, true, expectedRequest: request).WaitAsync(token);
        }, request.Token, operationName: "CaptureSelection");
        if (CaptureRequest == request)
            CaptureRequest = null;
    }

    /// <summary>Invalidates a pending capture when a newer editor or desktop command arrives.</summary>
    void CancelCapture()
    {
        Volatile.Read(ref CaptureRequest)?.Cancel();
    }

    /// <summary>Retries transient clipboard access, then reads through an independent STA fallback without blocking the UI.</summary>
    /// <param name = "token">Cancels this operation&apos;s checkpoints and bounded wait.</param>
    /// <returns>Fresh clipboard text from the primary or fallback read method.</returns>
    async Task<string?> ReadClipboardTextAsync(CancellationToken token)
    {
        try
        {
            return await Resilience.ExecuteResilientlyAsync(ct => ReadOnUiThreadAsync(() => Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.GetTextAsync(), ct), ClipboardReadCanRetry, token, retryCount: 2, attemptTimeout: TimeSpan.FromMilliseconds(250), totalTimeout: TimeSpan.FromMilliseconds(850), retryDelay: TimeSpan.FromMilliseconds(15), operationName: "ReadMauiClipboard");
        }
        catch (Exception primaryError)when (primaryError is not OperationCanceledException)
        {
            Log($"MAUI clipboard read failed; trying STA clipboard reader; error={primaryError}", Run: IsLogEnabled);
            try
            {
                return await Resilience.ExecuteResilientlyAsync(ReadClipboardOnStaThreadAsync, ClipboardReadCanRetry, token, retryCount: 1, attemptTimeout: TimeSpan.FromSeconds(2), totalTimeout: TimeSpan.FromSeconds(3), retryDelay: TimeSpan.FromMilliseconds(15), operationName: "ReadStaClipboard");
            }
            catch (Exception fallbackError)when (fallbackError is not OperationCanceledException)
            {
                throw new AggregateException("Both clipboard readers failed.", primaryError, fallbackError);
            }
        }
    }

    /// <summary>Selects only busy clipboard access and bounded read timeouts for replay-safe retries.</summary>
    /// <param name = "error">The retained final operation exception, if the operation failed.</param>
    /// <returns>Whether clipboard access can be replayed after a transient busy/timeout failure.</returns>
    static bool ClipboardReadCanRetry(Exception error) => error is Polly.Timeout.TimeoutRejectedException || error is ExternalException external && external.ErrorCode == HRESULT.CLIPBRD_E_CANT_OPEN.Value;
    /// <summary>Starts the MAUI clipboard read on its owning dispatcher and rejects a canceled completion.</summary>
    /// <param name = "operation">The clipboard read to start on its owning dispatcher.</param>
    /// <param name = "token">Cancels this operation&apos;s checkpoints and bounded wait.</param>
    /// <returns>The dispatched read result; cancellation prevents publishing a late value.</returns>
    Task<string?> ReadOnUiThreadAsync(Func<Task<string?>> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Dispatcher.HasThreadAccess)
            return operation().WaitAsync(token);
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Dispatcher.TryEnqueue(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                string? value = await operation().WaitAsync(token);
                token.ThrowIfCancellationRequested();
                completion.TrySetResult(value);
            }
            catch (OperationCanceledException)when (token.IsCancellationRequested)
            {
                completion.TrySetCanceled(token);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        }))
            throw new InvalidOperationException("Clipboard dispatcher is unavailable.");
        return completion.Task.WaitAsync(token);
    }

    /// <summary>Uses a private STA thread for the synchronous OLE clipboard API; it returns data without changing shared state.</summary>
    /// <param name = "token">Cancels this operation&apos;s checkpoints and bounded wait.</param>
    /// <returns>The private STA reader&apos;s text or its propagated read/cancellation failure.</returns>
    static Task<string?> ReadClipboardOnStaThreadAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                string text = Forms.Clipboard.GetText(Forms.TextDataFormat.UnicodeText);
                token.ThrowIfCancellationRequested();
                completion.TrySetResult(text);
            }
            catch (OperationCanceledException)when (token.IsCancellationRequested)
            {
                completion.TrySetCanceled(token);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "ClipboardFallback"
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task.WaitAsync(token);
    }

    /// <summary>Sends one Ctrl+C chord with generated Windows input declarations.</summary>
    static unsafe void SendCopy()
    {
        INPUT[] inputs = [new()
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new()
            {
                ki = new()
                {
                    wVk = VIRTUAL_KEY.VK_CONTROL
                }
            }
        }, new()
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new()
            {
                ki = new()
                {
                    wVk = VIRTUAL_KEY.VK_C
                }
            }
        }, new()
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new()
            {
                ki = new()
                {
                    wVk = VIRTUAL_KEY.VK_C,
                    dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP
                }
            }
        }, new()
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new()
            {
                ki = new()
                {
                    wVk = VIRTUAL_KEY.VK_CONTROL,
                    dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP
                }
            }
        }

        ];
        if (PInvoke.SendInput(inputs, sizeof(INPUT)) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        Log($"Ctrl+C sent; inputCount={inputs.Length}", Run: IsLogEnabled);
    }

    /// <summary>Exits the application for a middle click or the explicit tray command.</summary>
    void Exit() => ExitTask = ExitAsync();
    /// <summary>Stops process termination unless preferences and the explicit final diagnostic have been durably written.</summary>
    /// <returns>Completion of durable settings/log persistence and native termination, or a contained deferred exit.</returns>
    async Task ExitAsync()
    {
        var owner = TranslationWorkspace.CurrentRequest;
        var outcome = await ExceptionBoundary.RunSafelyAsync(async token =>
        {
            if (!await Preferences.SaveAsync().WaitAsync(token))
                throw new IOException("Exit deferred because settings were not persisted.");
            Log("Application exit requested", Run: IsLogEnabled);
            if (!await Logger.CompleteAsync("Final application shutdown record", token).WaitAsync(token))
                throw new IOException("Exit deferred because the final diagnostic could not be flushed to disk.");
            try
            {
                token.ThrowIfCancellationRequested();
                Dispose();
                Microsoft.Maui.Controls.Application.Current?.Quit();
            }
            catch
            {
                Logger.ResumeAfterFailedShutdown();
                throw;
            }
        }, owner.Token, operationName: "ExitApplication");
        if (outcome.Status == OperationStatus.Failed && !Disposed)
            Tray.ShowBalloonTip(4000, AppConstants.AppNameHumanReadable, Strings.ExitDeferred, Forms.ToolTipIcon.Error);
    }

    /// <summary>Releases the temporary capture, global shortcut and tray resources once.</summary>
    public void Dispose()
    {
        if (Disposed)
            return;
        Disposed = true;
        if (!ForegroundHook.IsNull)
        {
            PInvoke.UnhookWinEvent(ForegroundHook);
            ForegroundHook = default;
        }

        Presentation?.Cancel();
        Microsoft.UI.Xaml.Application.Current.UnhandledException -= OnUnhandledException;
        Window.AppWindow.Closing -= OnClosing;
        Window.Activated -= OnActivationChanged;
        LocManager.LanguageChanged -= RefreshTrayLanguage;
        TranslationWorkspace.RequestSuperseded -= CancelCapture;
        CancelCapture();
        PInvoke.UnregisterHotKey(new HWND(Handle), HotkeyId);
        Tray.Visible = false;
        Chrome?.Dispose();
        Tray?.Dispose();
        DestroyHandle();
        Log("Desktop disposed", Run: IsLogEnabled);
    }
}
