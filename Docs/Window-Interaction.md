# Window focus, dismissal and language selection

The retained WebView must be ready before requesting DOM focus. Home keeps an ElementReference for the input textarea and exposes its asynchronous FocusAsync action through the shared workspace bridge. DesktopHost requests focus only after activating the native window. A shortcut first copies from the previous application; input focus follows clipboard capture so Ctrl+C cannot copy from the translator itself. The template router's competing heading-focus component is removed. Completing a translation does not refocus the editor and therefore cannot interrupt typing in the output pane.

Window.Activated drives outside dismissal without a polling loop. A nonactivated capture preview is not dismissed before it has ever received activation. HideCore clears its logical visibility and activation flags before calling AppWindow.Hide, preventing the resulting deactivation event from recursively hiding again. The native window and its rendered WebView are retained.

SetLanguagesAsync normalizes user-selected collisions while holding the short workspace state lock. If the edited source is changed to the current concrete target, the target receives the previous source. The symmetric target change restores the previous target to the source. The existing two-Automatic restoration branch runs first and retains its separate history behavior. Automatic destination fallback during translation remains a distinct rule rather than a simulated manual selector change.

Controlled workspace tests cover both directions of tr/en exchange, a second de/fr pair, and both Automatic restoration sequences. Interactive focus and dismissal are observable through FocusInput and native activation records in the application log.

