namespace ResilientTranslator.MainProject.Constants.ThemeConstants
{
    /// <summary>
    /// Immutable design palette consumed by <see cref="AppColors"/> via <see cref="ThemeManager.Current"/>.
    /// Holds every color, gradient pair, and aurora backdrop stop the UI renders. Swap the active theme
    /// with <see cref="ThemeManager.Apply"/> and the whole UI re-skins on the next render.
    /// </summary>
    public sealed record AppTheme
    {
        public required Color Control { get; init; }
        public required Color ControlHover { get; init; }
        public required Color ControlBorder { get; init; }
        public required Color EditorBorder { get; init; }
        public required Color OutputBorder { get; init; }
        public required Color FocusBorder { get; init; }
        public required Color IconSurface { get; init; }
        public required Color IconBorder { get; init; }
        public required Color IconText { get; init; }
        public required Color WindowBorder { get; init; }
        public required Color CommandText { get; init; }
        public required Color BrandInk { get; init; }
        public required Color Overlay { get; init; }
        public required Color EditorText { get; init; }

        public required string Name { get; init; }

        // Page background — the aurora-lit night sky behind every screen.

        /// <summary> Deepest tone of the page background — the near-black night-sky color that shows where the aurora glow stops fade out. </summary>
        public required Color BackgroundDeep { get; init; }

        /// <summary> Slightly lifted background tone layered above <see cref="BackgroundDeep"/>; visible at the top of the viewport before the aurora colors take over. </summary>
        public required Color BackgroundBase { get; init; }

        // Brand colors — the two/three colors that identify the app.

        /// <summary> Main brand color — fills the start of PrimaryButton gradients, active tab indicators, link text, progress bars, and selected-item borders. </summary>
        public required Color Primary { get; init; }

        /// <summary> Lighter Primary — second stop in the active-tab gradient and decorative highlight tints. </summary>
        public required Color PrimaryLight { get; init; }

        /// <summary> Darker Primary — second stop in the page background gradient and deep card tints. </summary>
        public required Color PrimaryDark { get; init; }

        /// <summary> Secondary brand color — the warm coral paired with Primary in two-tone page and card gradients. </summary>
        public required Color Secondary { get; init; }

        /// <summary> Darker Secondary — gradient companion to <see cref="Secondary"/>. </summary>
        public required Color SecondaryDark { get; init; }

        /// <summary> Warm amber accent — decorative highlights and IconButton gradient endpoints, and any "pop" highlight that must catch the eye. </summary>
        public required Color Accent { get; init; }

        /// <summary> Darker Accent — gradient companion to <see cref="Accent"/>. </summary>
        public required Color AccentDark { get; init; }

        // Text colors — applied on top of the regular background (glass / aurora).

        /// <summary> Default body text — paragraph copy, card titles, list item labels. Sits on top of glass surfaces and the aurora backdrop. </summary>
        public required Color TextPrimary { get; init; }

        /// <summary> Subdued text — captions, helper lines under inputs, secondary chip labels, "X minutes ago" timestamps. </summary>
        public required Color TextSecondary { get; init; }
        /// <summary>Marks an intentionally offline translation policy.</summary>
        public Color OfflineStatus {get;init;}=Color.FromArgb("#8b9198");

        /// <summary> Lowest-emphasis text — placeholders inside empty inputs, disabled labels, ghost copy that should barely register. </summary>
        public required Color TextMuted { get; init; }

        // Glass surfaces — frosted cards layered over the aurora backdrop.

        /// <summary> Lightest glass tint — barely-visible film for subtle surface separation (input fields, low-emphasis chips). </summary>
        public required Color SurfaceSubtle { get; init; }

        /// <summary> Standard glass tint — the default frosted-white fill of CoreCard / GlassCard surfaces. </summary>
        public required Color SurfaceNormal { get; init; }

        /// <summary> Heaviest glass tint — modal headers, sticky toolbars, surfaces that must stand out from regular cards. </summary>
        public required Color SurfaceStrong { get; init; }

        /// <summary> Glass tint with a Primary-colored cast — applied to cards representing "current user" or "active selection". </summary>
        public required Color SurfaceTintPrimary { get; init; }

        /// <summary> Glass tint with an Accent-colored cast — used on emphasized surfaces to thematically connect them to <see cref="Accent"/>. </summary>
        public required Color SurfaceTintAccent { get; init; }

        /// <summary> Translucent dark film — modal backdrops dimming the page underneath, and shaded card insets. </summary>
        public required Color SurfaceDarken { get; init; }

        // Glass borders — the highlights and shadows that give cards depth.

        /// <summary> Upper highlight stroke of a glass card — mimics a window's reflective top edge under light. </summary>
        public required Color GlassBorderTop { get; init; }

        /// <summary> Lower shadow stroke of a glass card — the dimmer bottom edge that anchors the card visually. </summary>
        public required Color GlassBorderBottom { get; init; }

        /// <summary> Single average glass stroke — used where top/bottom asymmetry isn't worth the cost. </summary>
        public required Color GlassBorderDefault { get; init; }

        // Opaque overlays — solid fills that fully cover what's beneath.

        /// <summary> Solid fill of floating tooltip bubbles — opaque enough to keep tooltip text legible over any surface beneath. </summary>
        public required Color TooltipBackground { get; init; }


        // Semantic status colors.

        /// <summary> Positive-result green — successful operations and completion indicators. </summary>
        public required Color Success { get; init; }

        /// <summary> Darker Success — gradient endpoint for Success-themed buttons and chip backgrounds. </summary>
        public required Color SuccessDark { get; init; }

        /// <summary> Destructive / error red — DangerButton gradient start, validation error icons, failed operations. </summary>
        public required Color Error { get; init; }

        /// <summary> Darker Error — gradient endpoint for DangerButton and error-state chip backgrounds. </summary>
        public required Color ErrorDark { get; init; }

        /// <summary> Caution amber — warning toasts, "are you sure?" prompts, attention prompts. </summary>
        public required Color Warning { get; init; }

// Composite assets — gradient lists and backdrop stops.

        /// <summary> Optional named-surface gradient pairs for custom cards and palette tooling. </summary>
        public (Color Start, Color End)[] SurfaceGradients { get; init; } = [];

        /// <summary> Radial-gradient stops blurred under every glass surface to form the aurora backdrop. Position and Size use CSS background-position / background-size syntax. </summary>
        public required AuroraStop[] AuroraStops { get; init; }

        // Foreground text aliases.
        // These describe text that sits on top of a saturated fill (button gradient, toast background,
        // colored chip), as opposed to TextPrimary/Secondary/Muted which sit over the regular background.
        // Mixing the two layers was the original Surface-as-text-color bug that made DangerButton's label
        // bleed red.

        /// <summary>
        /// Canonical "on filled surface" text color — solid white, legible over Primary, Secondary, Accent, Error, Success, and Warning fills.
        /// All <c>TextOn*</c> and <c>EmojiOn*</c> aliases resolve to this; override an individual channel only if a specific filled surface needs a non-white legend (e.g. a light-amber button needing dark text).
        /// </summary>
        public Color TextOnFilledSurface => Colors.White;

        /// <summary> Text painted onto Primary / Secondary / Accent gradient buttons — PrimaryButton label, selected controls and active-tab labels. </summary>
        public Color TextOnAccent => TextOnFilledSurface;

        /// <summary> Text painted onto Error / Danger gradient buttons (DangerButton "Delete Account") and error toasts. </summary>
        public Color TextOnDanger => TextOnFilledSurface;

        /// <summary> Text painted onto Success gradient buttons and success toasts. </summary>
        public Color TextOnSuccess => TextOnFilledSurface;

        /// <summary> Text painted onto Warning-filled buttons or alert banners. </summary>
        public Color TextOnWarning => TextOnFilledSurface;

        /// <summary> Clickable inline-link color drawn over the regular background (not on a filled surface) — e.g. "Forgot password?". </summary>
        public Color TextLink => Primary;

        /// <summary> Inline error-message text drawn over the regular background — form validation hints, "this field is required" lines. For text on a red fill use <see cref="TextOnDanger"/>. </summary>
        public Color TextDanger => Error;

        /// <summary> Inline success-message text drawn over the regular background — "Saved", "Synced", "Up to date". </summary>
        public Color TextSuccess => Success;

        /// <summary> Inline warning-message text drawn over the regular background. </summary>
        public Color TextWarning => Warning;

        // Foreground emoji / monochrome glyph aliases.
        // Native color emoji ignore CSS color, but monochrome font-icon variants (Segoe UI Emoji on
        // Windows, masked emoji on iOS) and decorative spans we tint manually do honor it. These exist
        // for those cases — and so emoji usage reads with clear intent at the call site.

        /// <summary> Emoji painted on a Primary / Secondary / Accent fill — mirrors <see cref="TextOnAccent"/>. </summary>
        public Color EmojiOnAccent => TextOnAccent;

        /// <summary> Emoji painted on a Danger / Error fill — mirrors <see cref="TextOnDanger"/>. </summary>
        public Color EmojiOnDanger => TextOnDanger;

        /// <summary> Emoji painted on a Success fill — mirrors <see cref="TextOnSuccess"/>. </summary>
        public Color EmojiOnSuccess => TextOnSuccess;

        /// <summary> Emoji painted on a Warning fill — mirrors <see cref="TextOnWarning"/>. </summary>
        public Color EmojiOnWarning => TextOnWarning;

        /// <summary> Decorative emoji tinted with Accent — decorative highlights. </summary>
        public Color EmojiAccent => Accent;

        /// <summary> Decorative emoji tinted with Primary — navigation and decorative glyphs. </summary>
        public Color EmojiPrimary => Primary;

        /// <summary> Decorative emoji tinted with Warning. </summary>
        public Color EmojiWarning => Warning;

        /// <summary> Decorative emoji tinted with Success — checkmarks, completion ticks. </summary>
        public Color EmojiSuccess => Success;

        /// <summary> Decorative emoji tinted with Error — delete icons, alert glyphs. </summary>
        public Color EmojiDanger => Error;

        // Convenience aliases (kept for back-compat with earlier token names).

/// <summary> Aliased to <see cref="SurfaceNormal"/> — use this ONLY for glass-card backgrounds. For text on filled surfaces use <see cref="TextOnFilledSurface"/> (or one of the <c>TextOn*</c> aliases). </summary>
        public Color Surface => SurfaceNormal;

        /// <summary> Aliased to <see cref="SurfaceDarken"/> for back-compat with the original SurfaceDark token. </summary>
        public Color SurfaceDark => SurfaceDarken;

        /// <summary> Aliased to <see cref="BackgroundDeep"/> for back-compat with the original Background token. </summary>
        public Color Background => BackgroundDeep;
    }

    /// <summary> Single radial-gradient stop that paints part of the aurora backdrop. </summary>
    /// <param name="Color">The typed gradient-stop color.</param>
    /// <param name="Position">The original native window position captured at pointer-down.</param>
    /// <param name="Size">The original window or CSS-gradient dimensions.</param>
    public sealed record AuroraStop(Color Color, string Position, string Size);
}
