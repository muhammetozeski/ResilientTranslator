# English speech

Each editor has a speech button when its selected language is English or its existing automatic detection/result state resolves to English. TranslationWorkspace publishes EditorsChanged only when editor text or resolved language changes. SpeechEditor returns that existing state; the speech feature never invokes a language detector. Automatic source edits invalidate their previous detection until the current translation request reports a new result.

SpeechPlayback is a scoped C# service, shared by both buttons. Request ownership uses cancellation-source object identity. A new editor command stops the previous speech command; editing its text or changing its language away from English also stops that editor's speech. The button remains actionable during preparation, with animated waves, so the user can cancel preparation as well as playback. Idle opacity is 0.3 (70 percent transparent).

The supplied Paradee int8 ONNX model runs in a terminable Web Worker using the ONNX Web runtime already shipped for Töz. No second inference runtime or Python process is distributed. Windows EnglishPhonemizer serializes the bundled eSpeak NG pronunciation library off the UI thread; the eSpeak-to-Misaki conversion follows the supplied demo's American-English path. The selected model voice is its first American female voice. Original model bytes are copied without conversion. Licenses are included under Speech/licenses.

The browser controller retains one WAV recording and its text in RAM. Replaying that text skips both pronunciation and inference. When a new recording succeeds, the previous object URL is revoked and the recording is replaced. A canceled preparation terminates its worker; old worker messages cannot complete a newer request. Completed workers remain idle for reuse. Nothing writes synthesized audio to disk.

For a newly played text the playback count starts at one. Odd listens use rate 1.0; even listens use 0.9 with pitch preservation. Stopping playback retains the last completed recording. Model and player failures retain detailed logs, reset the failed worker for a retry, and expose a localized retry explanation on the button.

## Packaging and evidence

Speech model compression is excluded in the static-assets pipeline so the 8,462,119-byte model is shipped once. All newly added payload files, including pronunciation data, module compression variants and license texts, total approximately 9.77 MB. The measured 1.2.0 payload delta against the installed 1.1.0 build was approximately 6.74 MB; The older installation retained 4,085,641 bytes of obsolete .mjs variants, while common rebuilt files grew by 1,056,057 bytes. Comparing a dirty installed directory with a clean publish directory therefore understated the feature addition; use the added-file inventory when checking a feature size budget.

The actual C# phonemizer and original ONNX model produced 79,800 audio samples for the demo sentence. Controlled tests using the actual C# playback coordinator verified cache reuse, the 1.0/0.9/1.0 sequence and command cancellation. Browser-controller tests verified WAV retention, pitch-preserving playback, stop, worker termination, stale-worker rejection and URL disposal. These checks are distinct from listening in the native application.

