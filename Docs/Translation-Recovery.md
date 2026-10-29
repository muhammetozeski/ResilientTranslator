# Translation recovery and request ownership

## Where recovery belongs

The supplied TranslationEngine/ResilientTranslator.cs remains unchanged. It has internal provider fallback and a ten-second concurrent-pass cancellation window. It returns an error value when the pass fails. That return value must be converted into an exception inside a recoverable operation; throwing it only after leaving the retry boundary causes immediate UI failure instead of another attempt.

TranslationJob resolves the language on its background worker and delegates text recovery to TranslationRecovery. Each attempt creates a TranslationSession with an isolated copy of the original engine and its six HTTP clients. The session converts failed or empty engine output into an exception, then closes its own clients before retiring. Cancellation of an old attempt cannot close the next attempt's clients.

TranslationRecovery performs up to three concurrent calls through Polly, recreating the session each time. If this strategy is exhausted, it switches to the engine's sequential-provider mode for up to two calls. The original engine timeout is unchanged. Independent attempt and phase budgets bound the wait and cancel associated sessions. A permanent missing assembly is not treated as a transient network fault.

## Cancellation and publication

Both the actual operation and its WaitAsync receive the same inner token. A new RequestContext cancels old work; the UI publishes only while the request object is still the current owner. Late results are observed but cannot publish or enter the confirmed cache. Cleanup must remain private to the session even after a caller abandons its wait.

An unavailable or busy offline detector falls back to the original providers' automatic source detection. It does not fabricate a detected language. Destination fallback rules remain in TranslationWorkspace.

## Large input and confirmed results

Oversized text is divided using Unicode text-element boundaries and a 1,000-byte UTF-8 content budget. Original whitespace is carried separately and reassembled. A single text element larger than the budget is handled without recursive re-splitting. Partial chunk output is never published as a completed translation.

Only successful, nonempty results are eligible for the bounded in-memory cache. The key includes the complete source text and resolved language pair. Errors, partial results and abandoned successes are excluded. Confirmed-result reuse is an alternative to a new network request, not a fabricated success.

An explicit manual arrow command passes forceNetwork through TranslationInput to TranslationRecovery's bypassCache option, including recursively split chunks. It always starts a fresh request without typing debounce. Configurable wrapper budgets, retries and cache limits come from the reflected settings registry; the supplied engine's own constants remain unchanged.

## Diagnosing an exhausted request

Use the installed application's AppCache/Logs directory. Request, attempt and execution-mode metadata flows into engine and HTTP logs. Read the attempt history and the aggregate final error; do not infer a cause from the generic UI string.

Historical application logs showed the valid word araba, Turkish to English, waiting about 9.9 seconds in the HTTP connection queue before cancellation. The previous application performed only one engine pass. A later real-service probe returned car, establishing that the input itself was usable; it did not reproduce the historical network condition.

Controlled tests exercise transient failure followed by success, concurrent exhaustion followed by sequential success, exhaustion with all errors retained, missing deployment files, empty responses, cancellation of a function ignoring its token, late-result cache rejection, language-pair separation, Unicode chunking and the oversized-single-element guard. A separate consumer verifies the unchanged final error message when a deliberately failing engine exhausts every strategy. A model-free consumer verifies real provider-side detection when offline model loading fails.
