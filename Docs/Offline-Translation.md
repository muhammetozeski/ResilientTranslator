# Offline translation and progressive recovery

The shared application calls the original Bergamot JavaScript/WebAssembly engine and original Töz ONNX models through separate local JavaScript modules in the retained BlazorWebView. Production does not start Node.js or convert model formats. C# owns request lifetime, settings and publication; terminable Web Workers perform local inference.

## One normal execution policy

Every eligible engine starts immediately: Bergamot, Töz for a Turkish-containing pair, and the unchanged online engine. Results advance monotonically from Bergamot to Töz to online. A better result cancels lower-ranked work immediately; a new RequestContext cancels every older branch. Only the current request object may change editor text or status. Numeric transport IDs are diagnostics/interop handles, not C# ownership.

Gray mode disables the online branch completely, including connectivity checks. Network loss does not change modes or erase a successful local result. Failed online bursts retry indefinitely with a cancellable pause until success or request cancellation. Each burst still has finite attempt and phase budgets. Logs retain every attempt; the in-memory failure list is bounded.

Bergamot provides all twelve English-centric directions for the application's seven languages and composes non-English pairs through English. Töz uses the original bidirectional Turkish-English weights; Turkish/other-language requests combine a Töz leg with a Bergamot English leg. These chains race independently against the complete Bergamot translation.

## Worker lifetime and model assets

Workers are retained and primed while the window is hidden. Cancellation terminates the active worker, rather than leaving obsolete computation running behind a discarded Task. Pool capacity, beam sizes, supported thread count, model loading budgets, local recovery and model cache limits are advanced preferences. Browsers without shared-memory support use one ONNX thread per worker; independent workers still execute concurrently.

The original Bergamot WebAssembly build exhausted its memory when five model directions were retained in one worker. The production bridge therefore bounds each worker's model cache to at most two directions and replaces a worker when another language pair requires room. English pivot translation needs two directions.

`MainProject/Infrastructure/Offline/Prepare-OfflineAssets.ps1` downloads missing assets from the tracked manifest and verifies exact size and SHA-256. Publish calls this script before building. Model files live under `MainProject/wwwroot/offline/models`, are excluded from Git, and ship as application static assets. Model and runtime licenses are retained with their respective assets.

## Detection and failure recovery

The detector picker exposes full seven-language Lingua, MediaPipe, small fastText, TinyLD, TinyLD Light, Firefox CLD2, franc-min and CLD3. The comparison table is the measured 150-example sample: 130 Turkish/English examples and 20 examples across the other five languages. Lingua low-accuracy and full fastText are deliberately excluded.

A failed or inconclusive selected detector falls back to Lingua. If neither identifies the source, provider-side automatic detection remains available; unknown text is never assigned identical guessed source and target languages and published unchanged as a successful offline translation.

## Evidence

MediaPipe's supplied UMD loader must execute in a classic worker. Importing it as an ES module first hid ModuleFactory from the SDK, then exposed a strict-mode block-function error in custom_dbg when the factory was manually exported. Production uses a dedicated classic MediaPipe worker with the original loader unchanged; the API module is dynamically imported from that worker. Native startup warming then succeeded. A Node compatibility harness that evaluates imports as classic scripts can mask this browser difference.

The original production worker code translated all twelve Bergamot directions, an English-pivot pair and both Töz pivot directions in isolated browser-worker-compatible consumers. Real worker cancellation terminated old computation and allowed a newer request to finish. All seven browser detector implementations matched their original 150 reference predictions. Controlled consumers using the actual coordinator and resilience code verified progressive publication, lower-priority cancellation, gray-mode isolation and online retry cancellation. These checks do not replace a native WebView interaction trial.
