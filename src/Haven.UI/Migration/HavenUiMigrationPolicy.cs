namespace Haven.UI;

/// <summary>
/// Behaviour-preserving rules for moving an existing Haven surface onto HUI.
/// The default policy mirrors the repository's current migration contract.
/// </summary>
public sealed record HavenUiMigrationPolicy
{
    public static HavenUiMigrationPolicy Default { get; } = new();

    public bool PreserveProductFeatures { get; init; } = true;
    public bool PreservePageStructure { get; init; } = true;
    public bool PreserveNavigation { get; init; } = true;
    public bool PreserveInteractionFlows { get; init; } = true;
    public bool PreservePageAccents { get; init; } = true;
    public bool PreserveSpacingAndTypography { get; init; } = true;
    public bool PreserveThemePrinciples { get; init; } = true;
    public bool PreserveDesignLanguage { get; init; } = true;

    /// <summary>
    /// A migration may intentionally change existing presentation only when an
    /// authoritative mockup requires it or the old presentation is confirmed to
    /// be an accidental framework artefact rather than Haven product behaviour.
    /// </summary>
    public bool AllowsIntentionalVisualChange(
        bool hasAuthoritativeMockup,
        bool isVerifiedFrameworkArtifact) =>
        hasAuthoritativeMockup || isVerifiedFrameworkArtifact;
}
