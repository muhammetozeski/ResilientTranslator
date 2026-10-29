# Project guidance

Read README.md before changing the application. The supplied TranslationEngine/ResilientTranslator.cs must remain unchanged. Its original source is C:\E\KodlamaProjeleri\c#\ConsoleTest\ResilientTranslator.cs.

The shared editor and request state are in MainProject. Windows tray, shortcut and clipboard integration are in ResilientTranslator/Platforms/Windows. Ownership is represented by RequestContext object identity; numeric IDs are diagnostic labels only. New commands supersede previous work. Do not put blocking network, file or clipboard fallback calls on the UI thread.

Use Publish.ps1 for the Windows x64 portable output. Runtime user files belong under the portable UserData or AppCache roots, outside the published AppData payload. Keep generated output and user data out of Git.

Read [the documentation guide](Docs/Documentation-Guide.md) and [topic index](Docs/İçindekiler.md) before changing the relevant behavior. Retain new verified project knowledge in its topic and update affected links; do not duplicate source, logs or activity history.

For project handoff, follow [the Walkthrough guide](Walkthrough/Walkthrough-Rehberi.md), selecting dated records by filename. Add a new concise record when an observed result changes the context or lesson for the next developer.


UI styles belong in paired Css.razor components explicitly rendered by their owning component. Do not replace this architecture with scoped .razor.css files.


Follow [the active template architecture](Docs/Architecture.md). Reuse existing Core controls and the AppColors/AppMeasures/AppStyles centers while preserving the approved product appearance. Optional accounts and Firebase are excluded from this application.
