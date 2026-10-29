namespace ResilientTranslator.MainProject.Constants.ThemeConstants;

/// <summary>Preserves the approved translator appearance through the shared palette contract.</summary>
public static class AppThemes
{
    public static readonly AppTheme TranslatorDark = new()
    {
        Name = "Translator Dark",
            // [Rule] sen, yapay zeka gerizekalı olduğu için FromArgb fonksiyonunun ARGB şeklinde hex kodu beklediğini anlayamıyorsun ve RGBA giriyorsun.
            // bu yüzden FromArgb yerine FromRgba fonksiyonunu kullan. string girebilirsin ona.

            Accent = Color.FromRgba("#74cf9c"),
            AccentDark = Color.FromRgba("#74a587"),
            BackgroundBase = Color.FromRgba("#14171c"),
            BackgroundDeep = Color.FromRgba("#14171c"),
            BrandInk = Color.FromRgba("#112e26"),
            CommandText = Color.FromRgba("#c7d2df"),
            Control = Color.FromRgba("#252e38"),
            ControlBorder = Color.FromRgba("#3a4756"),
            ControlHover = Color.FromRgba("#35453e"),
            EditorBorder = Color.FromRgba("#343c48"),
            EditorText = Color.FromRgba("#ffffff"),
            Error = Color.FromRgba("#ef8d8d"),
            ErrorDark = Color.FromRgba("#ef8d8d"),
            FocusBorder = Color.FromRgba("#74a587"),
            GlassBorderBottom = Color.FromRgba("#3a4756"),
            GlassBorderDefault = Color.FromRgba("#3a4756"),
            GlassBorderTop = Color.FromRgba("#3a4756"),
            IconBorder = Color.FromRgba("#3a4b40"),
            IconSurface = Color.FromRgba("#242f2a"),
            IconText = Color.FromRgba("#a9d9bb"),
            OutputBorder = Color.FromRgba("#3b5145"),
            Overlay = Color.FromRgba("#00000077"),
            Primary = Color.FromRgba("#74cf9c"),
            PrimaryDark = Color.FromRgba("#112e26"),
            PrimaryLight = Color.FromRgba("#a9d9bb"),
            Secondary = Color.FromRgba("#a9d9bb"),
            SecondaryDark = Color.FromRgba("#3a4b40"),
            Success = Color.FromRgba("#93d5a8"),
            SuccessDark = Color.FromRgba("#74a587"),
            SurfaceDarken = Color.FromRgba("#00000077"),
            SurfaceNormal = Color.FromRgba("#1c2129"),
            SurfaceStrong = Color.FromRgba("#14171c"),
            SurfaceSubtle = Color.FromRgba("#14171c"),
            SurfaceTintAccent = Color.FromRgba("#35453e"),
            SurfaceTintPrimary = Color.FromRgba("#242f2a"),
            TextMuted = Color.FromRgba("#92a8ba"),
            TextPrimary = Color.FromRgba("#edf2f8"),
            TextSecondary = Color.FromRgba("#92a8ba"),
            TooltipBackground = Color.FromRgba("#252e38"),
            Warning = Color.FromRgba("#e5c880"),
            WindowBorder = Color.FromRgba("#4b5968"),
        AuroraStops = []
    };
}
