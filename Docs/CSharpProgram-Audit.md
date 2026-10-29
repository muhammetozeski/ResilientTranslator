# CSharpProgram compliance review

Scope: the active Windows application, shared first-party code, generator, launcher and local delivery scripts. Existing Web/mobile hosts are retained for a future requested target; their absence from Windows delivery is not a defect. The supplied TranslationEngine/ResilientTranslator.cs is explicitly protected and remains unchanged. Test fixtures and diagnostic tools consume production contracts rather than adding test hooks to lower layers.

## Skill requirements

| Requirement | Implementation and evidence |
|---|---|
| Existing project and architecture | Work stayed in the existing solution. Core controls, PageBase, MainLayout and the C# design centers remain active. Generic control capabilities were also ported to the source template without its translator-specific appearance. |
| Identity and paths | Constants/AppConstants is the single identity source. The launcher links the same source. AppPaths resolves UserData and AppCache outside AppData, independent of the working directory. A relocated launcher fixture preserved quoted/Unicode arguments. |
| Settings | The existing Setting serializer remains the one registry. SettingsPersistence adds bounded I/O retries, preservation of invalid input and a durable pending destination. Controlled primary-file locks verified fallback and promotion on reload; valid settings survived repair of an invalid value. |
| Localization | Seven source XML files live in languages and are embedded once. All share the same unique UI keys. Malformed packaged resources are backed up and restored; failed language changes retain the prior complete UI state. Application UI language is separate from translation selections. |
| Logging | Infrastructure/Logger.cs is the existing implementation moved out of the test-named folder, not a new logger. String diagnostics no longer expand character by character. Both memory views are bounded; persistent records remain complete. Emergency destinations preserve writer failures. |
| Shutdown | CompleteAsync queues an explicit last record, prevents later accepted records and flushes the actual primary or emergency file to disk. Failed file destinations reject completion. Native exit remains asynchronous and postpones termination on failure. |
| Recovery and cancellation | Polly remains the shared retry/budget implementation for replay-safe operations. RequestContext observes cancellation callback failures. Session cleanup attempts Dispose even if canceling a client fails. Renderer/event errors have an explicit recoverable boundary; native manual translation is independent of UI recovery. |
| Fonts | Font enumeration is on demand, private and cancelable at checkpoints. WPF enumeration has an independent GDI fallback and bounded recovery. The picker releases its list on close/disposal. Only the selected family is persisted; the complete family list is not logged or written. |
| Native integration | Native declarations use CsWin32, unused entries were removed, partially constructed controllers clean their owned resources and optional diagnostic callbacks cannot escape into Windows. |
| Delivery | Windows output is framework-dependent in the existing portable launcher/AppData layout. Both executables are signed before installation. Root debug-symbol leftovers are not distribution files. Publication is held by user instruction; language ZIP upload and post-release build-folder cleanup wait for that instruction to be lifted. |
| Documentation | Architecture and behavior contracts remain in Docs, with a dated Walkthrough record for the observations that changed recovery behavior. Active handwritten C# declarations were checked for summary, parameter and result descriptions. |

## Coding-principle review

| Principle | Applied interpretation and result |
|---|---|
| 1–4 | Existing responsibilities were reused; data-only wrappers and parallel managers were not introduced. Shared atomic replacement is one PortableFile operation; settings fallback coordinates the existing serializer. |
| 5–7 | New configurable limits and fixed values are at their class or operation boundary. Constants precede mutable state. Existing ready-engine APIs are not renamed solely for appearance. |
| 8–9 | Settings serialization, persistence recovery, logging, native hosting, UI and provider execution remain separate responsibilities. |
| 10 | New code, diagnostics and documentation are English. Product text is localized; protected existing Turkish source comments are retained. |
| 11 | New members use descriptive names rather than ambiguous abbreviations; native API type names remain the SDK's names. |
| 12–14 | Comments explain non-obvious ownership/recovery. XML contracts describe inputs and results; inherited contracts are retained. |
| 15 | The active application remains .NET 10/C# 14. The target and framework were not changed to force unrelated platform delivery. |
| 16 | Optional callbacks, attributes, resource paths, cancellation and failed results are handled at their integration boundaries. |
| 17–19 | New code follows the existing architecture and descriptive naming. Unused account/compiler dependencies were removed in the architectural migration. |
| 20–21 | The user requested project work rather than an isolated function. Shared imports and type aliases avoid repeating full method namespaces unnecessarily. |
| 22 | Native command routing and typed failure classification use existing switches where they clarify the decision. |
| 23 | Immutable data and fixed configuration use readonly/const; framework-bound parameters retain the properties required by their contracts. |
| 24 | Optional cancellation and ordinary control/persistence choices have defaults. Caller-facing operations retain short entry points. |
| 25–27 | Ownership, fallback, atomic commit and private abandoned work have explanatory comments without annotating ordinary assignments. |
| 28 | Missing functionality was implemented in the real shared controls and persistence/logging paths; no simulated success or placeholder wait was added. |
| 29 | New private implementation members use the language's default access where possible. |
| 30 | Polly is used for selected replay-safe file, provider and font operations. Clicks, swaps and process creation are not blindly replayed. |
| 31 | The protected translation source and source Rule comments were respected. Logger's existing Turkish comments were preserved. |
| 32–33 | Identity, paths, typed settings, UI strings, colors, measurements, effects and recovery decisions keep one authoritative source each. |
| 34 | Existing module initialization is not decorated with redundant commentary. |
| 35 | New files do not contain a redundant filename banner. |
| 36 | Process-wide shared state remains static where it already models the application; no singleton registration layer was added. |
| 37 | Repeated atomic text replacement and serialized settings recovery were combined rather than copied across callers. |
| 38 | Existing user comments were retained; newly corrected descriptions describe actual behavior. |
| 39–40 | API descriptions identify the actual snapshot, file, owner, resource or native callback being handled. |
| 41 | Source inspection, controlled tests, actual font enumeration and native user confirmation are distinguished. |
| 42 | Stateless helpers and callbacks remain static where their contracts permit it. |
| 43 | The real operation and its bounded wait receive the same token where supported. A physical settings writer retains its gate until it exits; an abandoned reader/font/provider result cannot update a superseded owner. |
| 44–45 | Non-obvious cleanup and failure-state behavior is documented. Existing Turkish Rule comments were not translated or removed. |
| 46 | Changes address concrete integration defects and controlled failures, rather than speculative timeout inflation. |
| 47 | No bin/obj deletion or stale-cache waiting was used as a code fix. |
| 48 | Polly/finally cleanup and their result handling preserve the original failures and actual recovery outcome. |
| 49 | Review used the supplied source and existing project context, including actual call sites. |
| 50 | Existing diagnostic coverage was retained and expanded; it was not removed to hide a failed path. |

The protected engine and unrequested hosts are explicit scope boundaries, not claims that every file in those independent implementations was rewritten to these rules. A passing static check is not a promise that external services or file systems can never fail.

Native shutdown was also checked on the installed application: after the user invoked tray middle-click, no application process remained and the final disk record was Final application shutdown record at 2026-10-29 22:44:16 Istanbul time.
