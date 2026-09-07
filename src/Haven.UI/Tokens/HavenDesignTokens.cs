namespace Haven.UI;

/// <summary>
/// Canonical HUI design-token vocabulary. These names intentionally match the
/// semantic string tokens already consumed by the existing HUI renderer so
/// adopting the typed API does not alter current UI behaviour.
/// </summary>
public static class HavenDesignTokens
{
    public static class Brushes
    {
        public static HavenBrushToken Accent { get; } = new("Accent");
        public static HavenBrushToken AccentSubtle { get; } = new("AccentSubtle");
        public static HavenBrushToken AccentHover { get; } = new("AccentHover");
        public static HavenBrushToken AccentSecondary { get; } = new("AccentSecondary");
        public static HavenBrushToken AccentSecondaryHover { get; } = new("AccentSecondaryHover");
        public static HavenBrushToken AccentMuted { get; } = new("AccentMuted");
        public static HavenBrushToken AccentTertiaryHover { get; } = new("AccentTertiaryHover");
        public static HavenBrushToken AccentGlow { get; } = new("AccentGlow");
        public static HavenBrushToken AccentSecondaryGlow { get; } = new("AccentSecondaryGlow");
        public static HavenBrushToken AccentTertiaryGlow { get; } = new("AccentTertiaryGlow");
        public static HavenBrushToken Surface { get; } = new("Surface");
        public static HavenBrushToken SurfaceRaised { get; } = new("SurfaceRaised");
        public static HavenBrushToken SurfaceSubtle { get; } = new("SurfaceSubtle");
        public static HavenBrushToken SurfaceSecondary { get; } = new("SurfaceSecondary");
        public static HavenBrushToken SurfaceElevated { get; } = new("SurfaceElevated");
        public static HavenBrushToken Overlay { get; } = new("Overlay");
        public static HavenBrushToken TextPrimary { get; } = new("TextPrimary");
        public static HavenBrushToken TextSecondary { get; } = new("TextSecondary");
        public static HavenBrushToken TextSoft { get; } = new("TextSoft");
        public static HavenBrushToken TextMuted { get; } = new("TextMuted");
        public static HavenBrushToken TextOnAccent { get; } = new("TextOnAccent");
        public static HavenBrushToken ButtonTextPrimary { get; } = new("ButtonTextPrimary");
        public static HavenBrushToken ButtonTextSecondary { get; } = new("ButtonTextSecondary");
        public static HavenBrushToken Border { get; } = new("Border");
        public static HavenBrushToken Shadow { get; } = new("Shadow");
        public static HavenBrushToken Warning { get; } = new("Warning");
        public static HavenBrushToken Danger { get; } = new("Danger");
        public static HavenBrushToken DangerHover { get; } = new("DangerHover");
        public static HavenBrushToken DangerGlow { get; } = new("DangerGlow");
        public static HavenBrushToken TextOnDanger { get; } = new("TextOnDanger");
        public static HavenBrushToken Transparent { get; } = new("Transparent");
        public static HavenBrushToken None { get; } = new("None");

        public static IReadOnlyList<HavenBrushToken> All { get; } =
        [
            Accent,
            AccentSubtle,
            AccentHover,
            AccentSecondary,
            AccentSecondaryHover,
            AccentMuted,
            AccentTertiaryHover,
            AccentGlow,
            AccentSecondaryGlow,
            AccentTertiaryGlow,
            Surface,
            SurfaceRaised,
            SurfaceSubtle,
            SurfaceSecondary,
            SurfaceElevated,
            Overlay,
            TextPrimary,
            TextSecondary,
            TextSoft,
            TextMuted,
            TextOnAccent,
            ButtonTextPrimary,
            ButtonTextSecondary,
            Border,
            Shadow,
            Warning,
            Danger,
            DangerHover,
            DangerGlow,
            TextOnDanger,
            Transparent,
            None
        ];
    }
}
