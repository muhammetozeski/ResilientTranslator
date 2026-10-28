# Resilient Translator

![Version](https://img.shields.io/badge/version-0.3.1-74cf9c) ![Platform](https://img.shields.io/badge/platform-Windows_11-2468a0) ![Runtime](https://img.shields.io/badge/.NET-10-512bd4)

A portable, borderless MAUI Blazor translator with a warm hidden window and two editable text panes.

- **Win + <** shows or hides the window. Opening sends Ctrl+C and translates fresh selected text immediately.
- Typing in either pane translates toward the other pane after one second without edits.
- Failed service passes retry through fresh sessions and then a sequential alternative. Large input is split without losing Unicode text or separators. Only confirmed successful output is reused from memory.
- A new command cancels the previous request; only the current request object may publish its result.
- Single left click on the tray icon toggles the window. Middle click exits.
- Header buttons independently swap the language pair or the two texts.
- Both language selectors offer Automatic. Offline Lingua detection reuses one preloaded seven-language detector. Selecting Automatic on the other side restores the first side to its last concrete language.
- Opening focuses the input editor; clicking outside dismisses the window. Choosing the other pane's concrete language exchanges the two language roles.
- The window stays above other windows. Its rendered WebView is retained while hidden.

## Install

Extract the release ZIP and start `ResilientTranslator.exe`. Windows x64 and the **.NET 10 Desktop Runtime** are required. Keep the launcher and its `AppData` folder together. Start with `--show` to open the initial preview; ordinary startup stays in the tray after its first render.

The executables are digitally signed. To let Windows verify the signature, run `Install-Certificate.cmd` from `SignatureTrust.zip` once. The programs run without it; only the signature stays unverified.

Settings and language files live in `UserData`; logs and the WebView cache live in `AppCache`. Both folders are relative to the portable program root.

## Source

The solution was created with the installed `MyMauiBlazorWebTemplate` template. The supplied translation engine is copied without changes into `TranslationEngine/ResilientTranslator.cs`. Each recoverable engine attempt owns an isolated engine instance and its HTTP clients; cancellation closes those clients without waiting on the UI thread. Clipboard capture is serialized, and results must still own their request before they can reach the editor.

Run `pwsh -File .\Publish.ps1` to produce the signed Windows package under `publish/ResilientTranslator`.



When an Automatic selection is used as the output destination, it becomes English. For reverse translation into an Automatic input, a nonempty input uses its last detected language; an empty input or missing detection falls back to English.



Developer knowledge: [documentation index](Docs/İçindekiler.md).




