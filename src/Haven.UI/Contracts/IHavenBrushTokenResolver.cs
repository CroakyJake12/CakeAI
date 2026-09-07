namespace Haven.UI;

/// <summary>
/// Contract implemented by platform backends that resolve HUI semantic brush
/// tokens into a backend-specific brush representation.
/// </summary>
/// <typeparam name="TBrush">The backend brush type.</typeparam>
public interface IHavenBrushTokenResolver<out TBrush>
{
    TBrush Resolve(HavenBrushToken token);
}
