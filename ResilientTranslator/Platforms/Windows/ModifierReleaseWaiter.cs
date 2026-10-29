using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
namespace ResilientTranslator;
/// <summary>Waits for Windows modifier release through a temporary keyboard event hook, without polling.</summary>
internal sealed class ModifierReleaseWaiter : IDisposable
{
    const bool IsLogEnabled = true;
    readonly TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly HOOKPROC Callback;
    HHOOK Hook;
    /// <summary>Subscribes only while a captured shortcut still has a held Windows modifier.</summary>
    unsafe ModifierReleaseWaiter()
    {
        Callback = OnKeyboard;
        Hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, Callback, new HINSTANCE(PInvoke.GetModuleHandle((string?)null).Value), 0);
        if (Hook.IsNull) throw new Win32Exception(Marshal.GetLastWin32Error());
        Log("Modifier release hook registered",Run:IsLogEnabled);
        if (!WindowsKeyDown()) Released.TrySetResult();
    }
    /// <summary>Completes immediately for released keys, otherwise awaits keyboard release notifications.</summary>
    /// <param name="token">Cancels this operation&apos;s checkpoints and bounded wait.</param>
    /// <returns>Completion when both Windows modifiers are released, or caller cancellation.</returns>
    public static async Task WaitAsync(CancellationToken token = default)
    {
        if (!WindowsKeyDown()) return;
        using var waiter = new ModifierReleaseWaiter();
        await waiter.Released.Task.WaitAsync(token);
    }
    /// <summary>Reads the modifier state once at subscription time.</summary>
    /// <returns>Whether either Windows modifier is currently held.</returns>
    static bool WindowsKeyDown() => PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_LWIN) < 0 || PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_RWIN) < 0;
    /// <summary>Completes on the last Windows-key release and forwards every event unchanged.</summary>
    /// <param name="code">The requested UI culture code or native hook status supplied by the caller.</param>
    /// <param name="message">The complete diagnostic text or native Windows message identifier.</param>
    /// <param name="data">The native message lParam value carrying its documented window/input payload.</param>
    /// <returns>The result of forwarding the keyboard event to the next Windows hook.</returns>
    unsafe LRESULT OnKeyboard(int code, WPARAM message, LPARAM data)
    {
        if (code >= 0 && (message.Value == PInvoke.WM_KEYUP || message.Value == PInvoke.WM_SYSKEYUP))
        {
            var key = ((KBDLLHOOKSTRUCT*)data.Value)->vkCode;
            if ((key == (uint)VIRTUAL_KEY.VK_LWIN && PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_RWIN) >= 0) ||
                (key == (uint)VIRTUAL_KEY.VK_RWIN && PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_LWIN) >= 0)) Released.TrySetResult();
        }
        return PInvoke.CallNextHookEx(default, code, message, data);
    }
    /// <summary>Removes the event hook as soon as capture completes or is canceled.</summary>
    public void Dispose() { if (!Hook.IsNull) { bool removed=PInvoke.UnhookWindowsHookEx(Hook); LogSafely($"Modifier release hook removed; success={removed}; nativeError={(removed ? 0 : Marshal.GetLastWin32Error())}",Run:IsLogEnabled); Hook = default; } GC.KeepAlive(Callback); }
}

