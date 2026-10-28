# Purpose of the project knowledge

This application combines a protected translation-engine source file, isolated provider sessions, an editable bidirectional UI and caller cancellation. A timeout inside the supplied engine does not itself implement application-level retries. Losing that distinction caused the UI to show failure after one exhausted engine pass.

The knowledge base preserves these project-specific relationships and the tests that established them. It should help the next developer find the actual failing layer rather than increase a timeout, hide a failure or repeat the same investigation. Walkthrough retains the concise event and its consequence; Docs retains the reusable method.
