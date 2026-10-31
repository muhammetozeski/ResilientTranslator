# Offline translation integration decision

Agreed with the user on 2026-10-31; implementation is pending.

Use Bergamot for offline translation. Reuse its supplied JavaScript/WebAssembly engine and original language models through the application's existing BlazorWebView. A small separate JavaScript module connects the engine to a C# translation service through Blazor JavaScript interop. Keep translation work in a Web Worker so it does not block the interface.

Do not embed executable JavaScript source in C# strings. Calling an exported JavaScript function by its string name through IJSRuntime is acceptable. The user accepts the small separate JavaScript bridge and the supplied engine's internal JavaScript.

This approach does not require a separate Node.js runtime. Reuse the supplied engine rather than rewriting its inference or converting the models. Keep the integration consistent with the project's existing coding principles.

Engine source: https://github.com/browsermt/bergamot-translator

Mozilla's browser integration example: https://github.com/mozilla/translate
