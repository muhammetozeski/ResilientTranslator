# Project guidance

Read README.md before changing the application. The supplied TranslationEngine/ResilientTranslator.cs must remain unchanged. Its original source is C:\E\KodlamaProjeleri\c#\ConsoleTest\ResilientTranslator.cs.

The shared editor and request state are in MainProject. Windows tray, shortcut and clipboard integration are in ResilientTranslator/Platforms/Windows. Ownership is represented by RequestContext object identity; numeric IDs are diagnostic labels only. New commands supersede previous work. Do not put blocking network, file or clipboard fallback calls on the UI thread.

Use Publish.ps1 for the Windows x64 portable output. Runtime user files belong under the portable UserData or AppCache roots, outside the published AppData payload. Keep generated output and user data out of Git.
