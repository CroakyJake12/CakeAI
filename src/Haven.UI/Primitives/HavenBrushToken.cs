namespace Haven.UI;

/// <summary>
/// Backend-neutral semantic brush token used by HUI scenes and renderers.
/// The token name is the compatibility contract; platform backends decide how
/// that name maps to a native brush or resource.
/// </summary>
public sealed record HavenBrushToken
{
    public HavenBrushToken(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (var character in name)
        {
            if (char.IsWhiteSpace(character))
                throw new ArgumentException("HUI brush token names cannot contain whitespace.", nameof(name));
        }

        Name = name;
    }

    public string Name { get; }

    /// <summary>Adapts the typed token to the existing HUI drawing contract without changing token semantics.</summary>
    public HavenTokenBrush ToBrush() => new(Name);

    public override string ToString() => Name;
}
