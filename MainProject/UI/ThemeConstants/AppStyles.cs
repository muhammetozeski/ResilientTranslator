namespace ResilientTranslator.MainProject.Constants.ThemeConstants
{
    /// <summary> Density levels for <see cref="AppStyles.BuildAcrylicStyle"/>: each maps to one of the Surface* tokens. </summary>
    public enum AcrylicLevel
    {
        Subtle,
        Normal,
        Strong,
        TintPrimary,
        TintAccent
    }

    /// <summary> Builds reusable CSS fragments — shadows, glows, the glass/acrylic surface block and the aurora backdrop — from the active theme tokens. </summary>
    public static class AppStyles
    {
        /// <summary> Builds a CSS shadow string (x/y offset, blur radius, color, opacity); shared by both box-shadow and text-shadow. </summary>
        /// <param name="x">The horizontal shadow offset or original pointer coordinate.</param>
        /// <param name="y">The vertical shadow offset or original pointer coordinate.</param>
        /// <param name="blur">The backdrop blur radius in CSS pixels.</param>
        /// <param name="color">The typed gradient-stop color.</param>
        /// <param name="opacity">The requested alpha fraction between zero and one.</param>
        /// <returns>An invariant CSS shadow value with the supplied geometry and color.</returns>
        public static string GetShadow(int x, int y, int blur, Color color, float opacity)
            => $"{x}px {y}px {blur}px rgba({(int)(color.Red * 255)}, {(int)(color.Green * 255)}, {(int)(color.Blue * 255)}, {opacity.ToString(System.Globalization.CultureInfo.InvariantCulture)})";

        /// <summary>Builds the approved settings shadow from the same overlay color as its backdrop.</summary>
        public static string SettingsShadow => $"0 12px 40px {AppColors.Overlay.ToRgbaHex(true)}";

        /// <summary>Escapes a user-selected font and renders its invariant size and typed theme color.</summary>
        /// <param name="family">The selected installed font-family name to escape for CSS.</param>
        /// <param name="size">The original window or CSS-gradient dimensions.</param>
        /// <returns>Invariant CSS for the selected family, size and shared editor-text color.</returns>
        public static string BuildEditorTypography(string family, double size) =>
            FormattableString.Invariant($"font-family:'{family.Replace("\\", "\\\\").Replace("'", "\\'")}';font-size:{size}px;color:{AppColors.EditorText.ToRgbaHex(true)}");
        public static string ShadowSoft => GetShadow(0, 4, 12, Colors.Black, 0.05f);
        public static string ShadowMedium => GetShadow(0, 10, 25, Colors.Black, 0.15f);
        public static string ShadowHeavy => GetShadow(0, 20, 40, Colors.Black, 0.25f);
        public static string ShadowPrimaryGlow => GetShadow(0, 8, 20, AppColors.Primary, 0.3f);
        public static string ShadowErrorGlow => GetShadow(0, 10, 20, AppColors.Error, 0.4f);

        /// <summary> Same shape as <see cref="ShadowPrimaryGlow"/> but tinted by an arbitrary color so gradient cards can glow in their own hue. </summary>
        /// <param name="tint">The typed color used to derive a glow.</param>
        /// <param name="opacity">The requested alpha fraction between zero and one.</param>
        /// <returns>A CSS shadow using the requested tint and opacity.</returns>
        public static string GetGlow(Color tint, float opacity = 0.3f) => GetShadow(0, 8, 20, tint, opacity);

        /// <summary> Black drop / text shadow — the common case, so call sites skip passing Colors.Black each time. </summary>
        /// <param name="x">The horizontal shadow offset or original pointer coordinate.</param>
        /// <param name="y">The vertical shadow offset or original pointer coordinate.</param>
        /// <param name="blur">The backdrop blur radius in CSS pixels.</param>
        /// <param name="opacity">The requested alpha fraction between zero and one.</param>
        /// <returns>An invariant CSS shadow value with the supplied geometry and color.</returns>
        public static string GetShadow(int x, int y, int blur, float opacity) => GetShadow(x, y, blur, Colors.Black, opacity);

        /// <summary> Translucent black darkening layer for image overlays and modal backdrops — one central "scrim" so every dimming surface stays consistent. </summary>
        /// <param name="opacity">The requested alpha fraction between zero and one.</param>
        /// <returns>The theme-independent translucent black CSS color used for dimming.</returns>
        public static string Scrim(float opacity) => Colors.Black.WithAlpha(opacity).ToRgbaHex(true);

        /// <summary>
        /// Darkness of the full-screen modal backdrop. A dark scrim (not a white film) so the layer reads as real
        /// glass that mixes the colors behind it, rather than a misty white wash. Kept moderate so the blurred,
        /// saturated page colors still show through as a dim glassy backdrop that makes the dark card pop.
        /// </summary>
        const float BackdropScrimOpacity = 0.4f;

        /// <summary>
        /// Builds the CSS for the full-screen layer that sits BEHIND a modal card: a normal backdrop blur plus a
        /// dark scrim and boosted saturation, so the whole viewport reads as color-mixing glass (never a foggy
        /// white haze). This is the glass over the viewport, not the card's own surface. One central definition.
        /// </summary>
        /// <param name="blur">The backdrop blur radius in CSS pixels.</param>
        /// <returns>CSS declarations for the modal&apos;s dimmed and blurred backdrop.</returns>
        public static string BuildBackdropFrost(int blur = AppMeasures.Blur.Normal)
            => $"background:{Scrim(BackdropScrimOpacity)};" +
               $"backdrop-filter:blur({blur}px) saturate(160%);-webkit-backdrop-filter:blur({blur}px) saturate(160%);";

        /// <summary>
        /// Opacity of the DARK frost layer painted under the <see cref="AcrylicLevel.Strong"/> surface tint.
        /// Modal / dialog / error-card bodies need a dark, high-contrast surface so their light text stays
        /// readable; stacking this deep film of the theme's background colour beneath the translucent tint makes
        /// the card read as dark glass (still translucent, never an opaque cover, never a coloured tint).
        /// </summary>
        const float StrongFrostDarkOpacity = 0.82f;

        /// <summary>
        /// Produces a full glass-morphism CSS declaration block: translucent background, blur, hairline
        /// border, and dual inset highlight/shadow that fakes the top-edge light reflection of real glass.
        /// Always prefer this over hand-rolling backdrop-filter + rgba — it stays theme-correct.
        /// The <see cref="AcrylicLevel.Strong"/> level (modal / dialog / error surfaces) additionally
        /// stacks a pure-white frost under the surface tint so its content reads clearly.
        /// </summary>
        /// <param name="level">The semantic acrylic surface intensity.</param>
        /// <param name="blur">The backdrop blur radius in CSS pixels.</param>
        /// <returns>A CSS declaration block derived from the active semantic surface and blur.</returns>
        public static string BuildAcrylicStyle(AcrylicLevel level = AcrylicLevel.Normal, int blur = AppMeasures.Blur.Normal)
        {
            Color bg = level switch
            {
                AcrylicLevel.Subtle => AppColors.SurfaceSubtle,
                AcrylicLevel.Strong => AppColors.SurfaceStrong,
                AcrylicLevel.TintPrimary => AppColors.SurfaceTintPrimary,
                AcrylicLevel.TintAccent => AppColors.SurfaceTintAccent,
                _ => AppColors.SurfaceNormal
            };

            // For the Strong level, paint the translucent surface tint OVER a deep dark film so modal/dialog/error
            // cards read as dark glass — readability comes from a dark, high-contrast base under the tint (still
            // translucent). Other levels keep the single thin tint that gives them their lighter frosted look.
            string background = level == AcrylicLevel.Strong
                ? $"linear-gradient({bg.ToRgbaHex(true)}, {bg.ToRgbaHex(true)}), " +
                  $"linear-gradient({AppColors.BackgroundDeep.WithAlpha(StrongFrostDarkOpacity).ToRgbaHex(true)}, {AppColors.BackgroundDeep.WithAlpha(StrongFrostDarkOpacity).ToRgbaHex(true)})"
                : bg.ToRgbaHex(true);

            return $"background:{background};" +
                   $"backdrop-filter:blur({blur}px) saturate(140%);-webkit-backdrop-filter:blur({blur}px) saturate(140%);" +
                   $"border:1px solid {AppColors.GlassBorderDefault.ToRgbaHex(true)};" +
                   $"box-shadow:inset 0 1px 0 {AppColors.GlassBorderTop.ToRgbaHex(true)}," +
                   $"inset 0 -1px 0 {AppColors.GlassBorderBottom.ToRgbaHex(true)},{ShadowSoft};";
        }

        /// <summary> The frosted bar surface shared by the top header and the bottom navigation: the central acrylic fill, rounded inner corners, and a single hairline on the edge facing the content (the other three borders are dropped because they sit on the screen edges). </summary>
        /// <param name="pinnedToBottom">Selects bottom-facing rather than top-facing bar corners and borders.</param>
        /// <returns>CSS for the chosen top or bottom shared bar surface.</returns>
        public static string BuildBarSurface(bool pinnedToBottom)
        {
            string innerEdge = pinnedToBottom
                ? $"border-top-left-radius:{AppMeasures.Radius.XLarge}px;border-top-right-radius:{AppMeasures.Radius.XLarge}px;border-bottom:none;"
                : $"border-bottom-left-radius:{AppMeasures.Radius.XLarge}px;border-bottom-right-radius:{AppMeasures.Radius.XLarge}px;border-top:none;";
            return BuildAcrylicStyle(AcrylicLevel.Subtle, AppMeasures.Blur.Strong) + innerEdge + "border-left:none;border-right:none;";
        }

        /// <summary>
        /// Stacks the active theme's <see cref="Theme.AuroraStop"/>s as overlapping radial gradients on top of
        /// a vertical base gradient. Three soft blooms give backdrop-filter blur the contrast it needs
        /// to read as real frosted glass instead of a flat color.
        /// </summary>
        /// <returns>The active backdrop CSS, including a plain gradient when no radial stops are configured.</returns>
        public static string BuildAuroraBackground()
        {
            string radials = string.Join(", ", AppColors.AuroraStops.Select(s =>
                $"radial-gradient(ellipse {s.Size} at {s.Position}, {s.Color.WithAlpha(0.55f).ToRgbaHex(true)} 0%, transparent 70%)"));
            return $"background:{(radials.Length > 0 ? radials + ", " : "")}linear-gradient(180deg, {AppColors.BackgroundBase.ToRgbaHex(true)} 0%, {AppColors.BackgroundDeep.ToRgbaHex(true)} 100%);";
        }

    }
}
