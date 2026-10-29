# Window focus, dismissal and language selection

## Full-client frame and pointer ownership

BorderlessWindow.cs is a reusable Windows MAUI adapter in the MauiWindowing namespace. Options at the top configure minimum logical dimensions, topmost behavior and application callbacks. Construct it from OnWindowCreated and call AttachBrowserAsync from BlazorWebViewInitialized. The caller retains and disposes it with the native window.

It adapts UsefulNotification's verified full-client solution: remove caption/frame/edge styles, return zero from WM_NCCALCSIZE and prevent WinUI from restoring frame styles in WM_STYLECHANGING. The visible frame and bottom-right grip belong to HTML. A read-only measurement of the deployed application found equal window and client dimensions, both 1,000 by 560 pixels. The user confirmed the frame, movement, resizing and settings behavior in the 0.4.0 local build.

Reuse requires Microsoft.Windows.CsWin32, unsafe blocks and NativeMethods entries for SetWindowSubclass, RemoveWindowSubclass, DefSubclassProc, WM_NCCALCSIZE, WM_STYLECHANGING, STYLESTRUCT, GetWindowLongPtr, SetWindowLongPtr, SetWindowPos, DwmSetWindowAttribute, GetCursorPos, GetDpiForWindow, USER_DEFAULT_SCREEN_DPI, WINDOW_STYLE, WINDOW_EX_STYLE, SUBCLASSPROC, DWMNCRENDERINGPOLICY and DWM_WINDOW_CORNER_PREFERENCE. The file has no translator-service dependency; callbacks connect logging, commands and resize persistence.

The injected capture-phase handler starts left dragging only when the exact target carries data-window-background. Interactive descendants do not qualify. Middle dragging accepts every DOM target and suppresses pointer, mouse and auxiliary click delivery before application handlers. The native side owns one Gesture object. Pointer release, capture loss and blur end the gesture and cancel queued animation callbacks. Movement uses cursor deltas; it never enters a modal Windows move loop or polls continuously. Minimum resize limits are scaled by the window's actual display density.

The settings backdrop closes its dialog on a click while the dialog stops propagation. Native hiding also clears this transient dialog state before the next opening. This behavior is separate from outside-window dismissal.

## Clipboard capture

Opening requests Ctrl+C before activating the editor. Clipboard-change notification allows a newly copied selection to arrive within the existing configurable budget, but a timeout or failed native copy still proceeds to read the current clipboard. Existing text, including text copied by Snipping Tool, is valid input. Capture does not clear the clipboard, require a new sequence number or reject text merely because another clipboard change occurred during the read. Empty/nontext clipboard content starts no translation. Serialized access, request object identity and cancellation retain ownership of the accepted result; a newer command still supersedes the earlier capture. The primary asynchronous reader retains its retry policy and independent STA fallback.

For the compact toolbar, proportional splitter and settings contract, see [Preferences and layout](Preferences-and-Layout.md).

The retained WebView must be ready before requesting DOM focus. Home keeps the first CoreInput instance, which owns the native textarea ElementReference and exposes its asynchronous FocusAsync action through the shared workspace bridge. DesktopHost first verifies real foreground ownership, then focuses the native WebView host and finally requests DOM textarea focus. Completing a DOM focus call is not proof that Windows keyboard focus belongs to the application. Presentation-owned cancellation prevents a delayed focus request from undoing a later dismissal. A shortcut first copies from the previous application; input focus follows clipboard capture so Ctrl+C cannot copy from the translator itself. The template router's competing heading-focus component is removed. Completing a translation does not refocus the editor and therefore cannot interrupt typing in the output pane.

Native foreground-change notifications drive dismissal without polling. Owned browser children and popups resolve to the same root owner and are not treated as outside interaction. WinUI activation events are retained as a fallback, but logical activation alone is not treated as evidence of real foreground ownership. HideCore clears visibility before hiding, and cancels its presentation focus request to prevent reactivation by stale work.
SetLanguagesAsync normalizes user-selected collisions while holding the short workspace state lock. If the edited source is changed to the current concrete target, the target receives the previous source. The symmetric target change restores the previous target to the source. The existing two-Automatic restoration branch runs first and retains its separate history behavior. Automatic destination fallback during translation remains a distinct rule rather than a simulated manual selector change.

Controlled workspace tests cover both directions of tr/en exchange, a second de/fr pair, and both Automatic restoration sequences. Actual focused-edit observations are read through the accessibility event stream without driving the UI. Foreground ownership and dismissal are recorded separately in the application log.



In the 0.3.1 interaction probe, the accessibility focus event identified the input editor as Girdi with ControlType.Edit. The same presentation logged native host focus accepted and realForeground=True. Stale foreground notifications were ignored before any dismissal was queued.

