# Preferences, UI languages and editor layout

Settings.cs remains the setting registry and serializer. PreferenceAttribute connects a Setting<T> field to a nameof(Strings...) label, technical-section membership and numeric bounds. Preferences reflects registered fields once; UI handlers retain the actual setting object. The technical section renders current boolean and numeric settings without per-field handlers. Add a translated Strings field and its seven XML entries when exposing another preference.

Accepted edits change RAM state immediately. The shared asynchronous writer takes a complete immutable snapshot after acquiring its gate, then performs retryable atomic replacement off the dispatcher. SettingsPersistence preserves invalid input and uses a durable pending file if the primary is locked; the next load promotes the newest valid snapshot. An abandoned physical writer retains its gate until it exits. Resolved translation-language history uses this same writer. Font enumeration runs on demand on a worker and stays in the open picker only; only the selected family name is persisted.

## Application language

The Language setting controls the application UI and defaults to system. It is independent of translation source, target and startup defaults. LocManager captures the initial Windows user UI culture before any application language is applied; Automatic resolves that culture and falls back to English when unavailable. Explicit saved selections remain unchanged.

Seven complete XML files in the repository-root languages directory are embedded in MainProject: tr, en, de, fr, es, it and ru. The main package also contains their plain copies in AppData/languages. The settings picker discovers UserData language files on a worker whenever it opens, applies UI languages through a serialized language gate, updates tray labels and saves the successful selection. A complete lang.<culture-code>.xml with a valid .NET culture code can add another UI language. Custom files are retained and validated before applying; shipped-resource refresh only manages the seven shipped cultures. Earlier tests switched all seven UI languages and verified that translation selections were unchanged.

LocManager installs shipped resources into portable UserData. A different executable version or missing string key refreshes the resource, preserving the previous user file as .previous. Checking keys also handles a newer local build with the same version. All seven files are checked before normal startup and share the same complete nonempty string keys, including technical-setting labels.

## Status, manual translation and styles

StatusTicker is the same Razor component in the toolbar and settings preview. Its light is outside the moving viewport. Two fixed text spans and one animation provide a bounded loop; a ResizeObserver remeasures overflowing content and hover pauses it. The selected default is 70 pixels per second. The slider lives only in settings.

The manual arrow has a capture-phase native-browser command path. It reads the current input DOM value and calls the workspace with immediate=true and forceNetwork=true. This supersedes previous work and bypasses confirmed-result reuse, while retaining provider recovery and final failure reporting. It does not depend on the typing debounce or a completed old job. The hide command uses the same independent browser command routing.

UI styles use explicitly rendered Css.razor companions backed by AppColors, AppMeasures and AppStyles. MainLayoutCss owns the root background, HomeCss the translator surface, SettingsPanelCss the dialog, StatusTickerCss the status component and EditorSplitterCss the divider. Settings delegates backdrop behavior to CoreModal; editor values and native focus flow through CoreInput. See [Architecture](Architecture.md).

## Proportional divider

InputShare stores the left pane percentage, with 50 as the initial value. The grid uses two fractional tracks separated by an eight-pixel handle, so resizing the native window preserves the ratio. Pointer moves update CSS locally; only gesture completion persists the share. Keyboard arrow keys adjust one point and Home centers it. Settings provides a reset to 50 percent.

CenterSnapRange is an integer setting with default 6. The mouse path uses half its value on either side of 50: 47 through 53 maps to 50, and leaving that interval releases the snap. Controlled checks covered both boundaries, configurable range, persisted fractional values and reset. A browser layout check retained exactly 38 percent at widths of 1,000 and 600 pixels.

## Two-way automatic selection and recovery profiles

TranslationLanguageRules is the single ordered policy for startup normalization, selecting the other pane's language, disabled two-way-auto switching, automatic destination fallback and reverse edits using the last detected source. The existing behavior remains the default. Enabling two-way automatic selection permits both selectors to remain Automatic; the actual request direction resolves without changing those selectors.

Simple mode chooses the opposite language in the configured pair. Advanced mode evaluates ordered OR conditions and chooses the first matching target; unmatched inputs use the simple pair. The visual editor adds, removes and reorders these conditions. Unknown source detection stays provider-auto rather than fabricating a source language.

Recovery profiles configure only online burst budgets and retry pauses; every engine still starts immediately. Responsive, Balanced and Quality presets can be adjusted and saved under user-provided names. Invalid custom profile data remains recoverable, does not break the settings menu and is backed up before an explicit edit replaces it. Profile save failures remain visible. There is no total retry-count limit while a request is current.

The status light is a button with its prior visual footprint. Gray disables online translation; green indicates online participation, amber indicates pending work/retry and red indicates failure. Rich HTML tooltips cover controls and include a per-detector comparison table. The reusable CoreTooltip also exists in the source template; it uses paired Css.razor styles, C# interaction state and the shared CorePopup surface. Only the common element geometry bridge crosses into browser JavaScript.

Tooltip preferences are applied to the layout-owned PopupScope: enabled/disabled, opening delay (default1second) and fade-in duration (default0.6seconds). The durations remain editable through the reflected advanced settings; the enable switch is visible in the normal settings list. The seven UI language files include these labels. Disabling tooltips closes the active explanation and invalidates pending openings immediately.

Mouse-triggered tooltips open beside the latest cursor position captured during the wait, then stay still for reading; keyboard-triggered explanations retain element anchoring. CorePopup accepts a typed viewport PopupPoint and clamps either placement to the viewport. Normal explanation width is limited to260pixels; rich benchmark tables have a separate500pixel limit. Mouse motion updates C# state without rendering every pointer event; opacity animation belongs to Css.razor.

The detector menu's white buttons were a missing native-style reset in the Bare appearance, not a selector spelling error. CoreButtonClasses now supplies selector names to both rendered HTML and CoreButtonCss. The Bare reset uses zero specificity so feature borders, backgrounds and padding remain caller-owned.
