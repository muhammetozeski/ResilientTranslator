# Preferences, UI languages and editor layout

Settings.cs remains the setting registry and serializer. PreferenceAttribute connects a Setting<T> field to a nameof(Strings...) label, technical-section membership and numeric bounds. Preferences reflects registered fields once; UI handlers retain the actual setting object. The technical section renders current boolean and numeric settings without per-field handlers. Add a translated Strings field and its seven XML entries when exposing another preference.

Accepted edits change RAM state immediately. The shared asynchronous writer takes a complete immutable snapshot after acquiring its gate, then performs retryable atomic replacement off the dispatcher. Resolved translation-language history uses this same writer. Font enumeration runs on demand on a worker and stays in the open picker only; only the selected family name is persisted.

## Application language

The Language setting controls the application UI and defaults to tr. It is independent of translation source, target and startup defaults. Seven complete XML files in the repository-root languages directory are embedded in MainProject: tr, en, de, fr, es, it and ru. The settings picker applies UI languages off the dispatcher through a serialized language gate, updates tray labels and saves the successful selection. Tests switched all seven UI languages and verified that translation selections were unchanged.

LocManager installs shipped resources into portable UserData. A different executable version or missing string key refreshes the resource, preserving the previous user file as .previous. Checking keys also handles a newer local build with the same version. All seven files are checked before normal startup and share the same 57 nonempty string keys, including technical-setting labels.

## Status, manual translation and styles

StatusTicker is the same Razor component in the toolbar and settings preview. Its light is outside the moving viewport. Two fixed text spans and one animation provide a bounded loop; a ResizeObserver remeasures overflowing content and hover pauses it. The selected default is 70 pixels per second. The slider lives only in settings.

The manual arrow has a capture-phase native-browser command path. It reads the current input DOM value and calls the workspace with immediate=true and forceNetwork=true. This supersedes previous work and bypasses confirmed-result reuse, while retaining provider recovery and final failure reporting. It does not depend on the typing debounce or a completed old job. The hide command uses the same independent browser command routing.

UI styles use explicitly rendered Css.razor companions backed by AppColors, AppMeasures and AppStyles. MainLayoutCss owns the root background, HomeCss the translator surface, SettingsPanelCss the dialog, StatusTickerCss the status component and EditorSplitterCss the divider. Settings delegates backdrop behavior to CoreModal; editor values and native focus flow through CoreInput. See [Architecture](Architecture.md).

## Proportional divider

InputShare stores the left pane percentage, with 50 as the initial value. The grid uses two fractional tracks separated by an eight-pixel handle, so resizing the native window preserves the ratio. Pointer moves update CSS locally; only gesture completion persists the share. Keyboard arrow keys adjust one point and Home centers it. Settings provides a reset to 50 percent.

CenterSnapRange is an integer setting with default 6. The mouse path uses half its value on either side of 50: 47 through 53 maps to 50, and leaving that interval releases the snap. Controlled checks covered both boundaries, configurable range, persisted fractional values and reset. A browser layout check retained exactly 38 percent at widths of 1,000 and 600 pixels.
