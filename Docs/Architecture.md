# Translator architecture

The application uses MyMauiBlazorWebTemplate's shared architecture while preserving the approved translator appearance. The original source template is C:\E\KodlamaProjeleri\Templates\MyMauiBlazorWebTemplate. New features should reuse the relevant contract; a template library component need not appear on every screen.

## Rendered path

The MAUI host mounts MainProject.UI.Routes. Routes selects MainLayout, which renders MainLayoutCss and PageBaseCss and cascades ThemeManager.Current. Home inherits PageBase and explicitly renders HomeCss. The root owns navigation/theme subscriptions; PageBase releases its navigation subscription when disposed.

UI/_Imports selects ThemeComponent as the default base for ordinary components and CSS companions. Immutable palette changes reach existing child instances through the cascading value. No changing key recreates the editor subtree.

Home uses CoreInput for both multiline text panes, IconButton/CoreButton for header commands and Stack for the swap-action group. SettingsPanel delegates dialog/backdrop ownership to CoreModal and uses the same CoreButton/CoreInput controls. EditorSplitter and StatusTicker remain distinct feature controls.

## Appearance and shared values

AppTheme/AppThemes define TranslatorDark, preserving the existing palette. AppColors forwards typed colors; AppMeasures owns the existing sizes, spacing and responsive breakpoints. AppStyles builds reusable effects and editor typography. There is no independent CSS color registry in HomeCss.

The selected editor color updates the shared EditorText palette role. Font family and size remain persisted user preferences and feed AppStyles.BuildEditorTypography. Calculated splitter fractions and Stack child-padding properties are geometry, not another palette.

MainLayoutCss owns the host reset/background. HomeCss owns the compact screen layout. SettingsPanelCss, StatusTickerCss and EditorSplitterCss are explicitly rendered companions. The current UI uses the template architecture, not its sample colors or acrylic appearance.

## Shared control extensions

CoreButton retains its standard presentation and adds Compact and Bare presentations through ControlAppearance. Extra native attributes are forwarded to the actual button. IconButton forwards those attributes and supports the existing compact icon size. This preserves independent data-native-command routing for manual translation and hiding.

CoreInput retains Value/ValueChanged and grouped single-line input. Multiline and standalone modes render one real textarea or input. OnInput/OnChange carry existing event arguments, and FocusAsync targets the actual native element. A newer input can arrive while an earlier callback is awaiting; old completion does not write the earlier value back.

CoreModal retains its standard surface and adds a caller-styled presentation, content/backdrop classes, extra attributes and keyboard handling. Settings uses that presentation to keep its exact flat appearance. The shared modal owns backdrop close and propagation rather than duplicating dialog interaction in SettingsPanel.

## State and native integration

TranslationWorkspace owns editor values and RequestContext object identity. Its local state/cancellation notifications remain domain-specific. MainEvents is the common theme notification path; these are separate responsibilities.

Windows tray, shortcut, clipboard and the reusable BorderlessWindow adapter remain in the Windows host. The independent manual-arrow command still captures the current input DOM value, supersedes earlier work, skips debounce and bypasses confirmed-cache reuse. Original translation-engine source is unchanged.

Application identity and portable settings location have one source in Constants/AppConstants. Settings, localization, logging and recovery continue using their existing implementations. No second AppConstants or Logger is introduced.

NavigationData now declares the one actual page as a named anonymous object. NavigationGenerator accepts that form and preserves its original named-tuple form for multi-page applications. Both retain compiler-checked duplicate names. The root URL and generated Home route resolve to the same screen. Optional bottom navigation is disabled centrally.

## Selected modules and target

This translator has no accounts or Firebase feature. Their unused services, profile/event models, configuration-independent constants and package references were removed. Empty native-event scaffolds and unrelated reward sounds were also removed. General reusable controls and panels remain available.

The generator owns its Roslyn dependencies; the shared runtime library does not carry duplicate direct compiler-package references. Only the Windows target is currently delivered. Web/mobile host work belongs to a future requested target.

## Evidence and boundaries

The architecture inspection checks root styles, theme propagation, style companions and duplicate identity centers. It does not prove every runtime path.

Controlled renderings of the actual Home and Settings components matched the pre-migration screenshots at 1000 by 560 and 500 by 400 pixels, including the host background. Component checks covered concurrent input ordering, stale completion rejection, native attributes, backdrop close, palette updates and preserved component instances. A read-only accessibility observation found actual keyboard focus in the input editor. The user also confirmed the local input, swap controls and settings worked correctly.

See [preferences and layout](Preferences-and-Layout.md), [window interaction](Window-Interaction.md) and [translation recovery](Translation-Recovery.md) for their separate contracts.

Renderer recovery is owned by RecoverableErrorBoundary, with independent native retry routing. See [CSharpProgram review](CSharpProgram-Audit.md) for persistence and durable shutdown changes.

## Shared elements and popup composition

CoreButton, CoreInput and CoreSelect derive from CoreElement. It retains the designated native element and exposes GetBoundsAsync, SetPositionAsync and FocusAsync. SetPositionAsync uses viewport coordinates by default; Absolute mode explicitly selects the CSS containing block. ElementHandle can also represent a separate trigger or panel when a component has multiple DOM elements. Existing ThemeComponent inheritance remains appropriate for components without one designated native element, including CSS companions.

CorePopup owns conditional surface rendering, modal/transient layers, anchor positioning, outside dismissal and keyboard dismissal. CoreModal keeps its existing public parameters and uses CorePopup with modal semantics. InfoPopover uses it for click-to-open content, while CoreTooltip owns hover/focus timing in C#. DetectorPicker also uses the same anchored surface. No tooltip-specific JavaScript event/visibility system remains.

PopupScope belongs to one MainLayout and keeps one active tooltip. Native controls receive descriptions through their shared Core wrappers. The only browser bridge is wwwroot/ui/element.js: bind an element object, read geometry, project an explicit position, and forward viewport events. A bound object reference is reused; application code does not query selectors on every call. Browser layout calculation and the initial framework reference resolution are separate costs from application reference lookup.

The generic controls and layout scope are also present in MyMauiBlazorWebTemplate. A framework-only consumer compiled the actual template controls and exercised twelve reference, modal, positioning and tooltip state cases. Native bridge checks covered geometry, positioning, viewport event coalescing and listener disposal. These controlled checks are distinct from a human visual review of the installed window.
