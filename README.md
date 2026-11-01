# Resilient Translator

![Version](https://img.shields.io/badge/version-1.3.0-74cf9c) ![Platform](https://img.shields.io/badge/platform-Windows_11-2468a0) ![Runtime](https://img.shields.io/badge/.NET-10-512bd4)

A portable, borderless MAUI Blazor translator with a warm hidden window and two editable text panes.

- **Win + <** shows or hides the window by default; advanced settings can record keyboard and mouse chords, including Mouse 4/5. Opening requests Ctrl+C and translates the current clipboard text, including text copied earlier when Ctrl+C makes no change.
- Typing in either pane translates toward the other pane after one second without edits.
- Failed service passes retry through fresh sessions and then a sequential alternative. Large input is split without losing Unicode text or separators. Only confirmed successful output is reused from memory.
- A new command cancels the previous request; only the current request object may publish its result.
- Bergamot, applicable Töz translation chains and online services start immediately. The first result appears immediately; higher-priority results replace it and cancel lower-priority computation. Online recovery continues until success or cancellation.
- Click the status light for gray mode: local translation only, without online requests. The package includes models for all seven supported languages, using English as a bridge where necessary.
- English editors offer local read-aloud with selectable Misaki + neural (default) or FYLs-G2P pronunciation and a cancelable preparation indicator. The last recording stays in RAM; successive listens alternate normal and slightly slower playback.
- Retained local translation, pronunciation, speech and detection engines are prepared centrally in the background. Silent speech warmup does not replace the user's cached recording; new user commands cancel warming.
- Single left click on the tray icon toggles the window. Middle click exits.
- A compact toolbar swaps languages or texts; its arrow starts a fresh translation immediately.
- Click the icon or title for persistent appearance, startup-language and technical settings. The application UI supports Turkish, English, German, French, Spanish, Italian and Russian.
- Drag the divider to resize text panes proportionally; its center snap and 50 percent reset are configurable.
- Drag blank background with the left button or anywhere with the middle button. The HTML corner grip resizes the borderless window.
- Both language selectors offer Automatic. Optional two-way automatic selection supports a simple language pair or ordered visual rules. With this option disabled, selecting Automatic on the other side restores the first side to its last concrete language.
- Choose among eight offline language detectors, with a measured comparison table. Configurable tooltips explain controls and settings in all seven UI languages; their default delay is one second with a 0.6-second fade.
- Opening focuses the input editor; clicking outside dismisses the window. Choosing the other pane's concrete language exchanges the two language roles.
- The window stays above other windows. Its rendered WebView is retained while hidden.

## Install

Extract the release ZIP and start `ResilientTranslator.exe`. Windows x64 and the **.NET 10 Desktop Runtime** are required. Keep the launcher and its `AppData` folder together. Start with `--show` to open the initial preview; ordinary startup stays in the tray after its first render.

The executables are digitally signed. To let Windows verify the signature, run `Install-Certificate.cmd` from `SignatureTrust.zip` once. The programs run without it; only the signature stays unverified.

On first launch, the UI follows the Windows user display language; an unsupported language falls back to English. A language selected in settings is retained. Automatic remains available to follow Windows again.

The main package includes all seven XML files under `AppData/languages`; embedded copies also prepare editable files in `UserData`. Add a complete `UserData/lang.<culture-code>.xml` to offer another UI language in settings. Use an existing file as a starting point and retain every string key. Custom language files are loaded from disk and preserved across updates.

Settings and editable language files live in `UserData`; logs and the WebView cache live in `AppCache`. Both folders are relative to the portable program root.

## Source

The solution was created with the installed `MyMauiBlazorWebTemplate` template. The supplied translation engine is copied without changes into `TranslationEngine/ResilientTranslator.cs`. Each recoverable engine attempt owns an isolated engine instance and its HTTP clients; cancellation closes those clients without waiting on the UI thread. Clipboard capture is serialized, existing text remains usable without a new clipboard event, and results must still own their request before they can reach the editor.

Run `pwsh -File .\Publish.ps1` to produce the signed Windows package under `publish/ResilientTranslator`.



With two-way automatic selection disabled, an Automatic output destination becomes English. For reverse translation into an Automatic input, a nonempty input uses its last detected language; an empty input or missing detection falls back to English.



Developer knowledge: [documentation index](Docs/İçindekiler.md).






