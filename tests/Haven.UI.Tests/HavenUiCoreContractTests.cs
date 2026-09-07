using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenUiCoreContractTests
{
    [Fact]
    public void Brush_tokens_preserve_existing_semantic_vocabulary()
    {
        var expected = new[]
        {
            "Accent",
            "AccentSubtle",
            "AccentHover",
            "AccentSecondary",
            "AccentSecondaryHover",
            "AccentMuted",
            "AccentTertiaryHover",
            "AccentGlow",
            "AccentSecondaryGlow",
            "AccentTertiaryGlow",
            "Surface",
            "SurfaceRaised",
            "SurfaceSubtle",
            "SurfaceSecondary",
            "SurfaceElevated",
            "Overlay",
            "TextPrimary",
            "TextSecondary",
            "TextSoft",
            "TextMuted",
            "TextOnAccent",
            "ButtonTextPrimary",
            "ButtonTextSecondary",
            "Border",
            "Shadow",
            "Warning",
            "Danger",
            "DangerHover",
            "DangerGlow",
            "TextOnDanger",
            "Transparent",
            "None"
        };

        Assert.Equal(expected, HavenDesignTokens.Brushes.All.Select(token => token.Name));
        Assert.Equal(expected.Length, HavenDesignTokens.Brushes.All.Select(token => token.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(new HavenTokenBrush("Accent"), HavenDesignTokens.Brushes.Accent.ToBrush());
    }

    [Fact]
    public void Brush_token_primitive_rejects_ambiguous_names()
    {
        Assert.Throws<ArgumentNullException>(() => new HavenBrushToken(null!));
        Assert.Throws<ArgumentException>(() => new HavenBrushToken(" "));
        Assert.Throws<ArgumentException>(() => new HavenBrushToken("Accent Primary"));
    }

    [Fact]
    public void Brush_resolver_contract_stays_backend_neutral()
    {
        IHavenBrushTokenResolver<string> resolver = new EchoBrushResolver();

        Assert.Equal("Surface", resolver.Resolve(HavenDesignTokens.Brushes.Surface));
        Assert.DoesNotContain(
            typeof(IHavenBrushTokenResolver<>).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Default_migration_policy_preserves_existing_surface_behaviour()
    {
        var policy = HavenUiMigrationPolicy.Default;

        Assert.True(policy.PreserveProductFeatures);
        Assert.True(policy.PreservePageStructure);
        Assert.True(policy.PreserveNavigation);
        Assert.True(policy.PreserveInteractionFlows);
        Assert.True(policy.PreservePageAccents);
        Assert.True(policy.PreserveSpacingAndTypography);
        Assert.True(policy.PreserveThemePrinciples);
        Assert.True(policy.PreserveDesignLanguage);
        Assert.False(policy.AllowsIntentionalVisualChange(false, false));
        Assert.True(policy.AllowsIntentionalVisualChange(true, false));
        Assert.True(policy.AllowsIntentionalVisualChange(false, true));
    }

    private sealed class EchoBrushResolver : IHavenBrushTokenResolver<string>
    {
        public string Resolve(HavenBrushToken token) => token.Name;
    }
}
