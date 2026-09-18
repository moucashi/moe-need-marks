namespace MoeNeedMarks.Shared;

/// <summary>Keep the last complete value visible until its replacement is ready.</summary>
public sealed class RefreshCache<T> where T : class
{
    public T? Value { get; private set; }
    public bool NeedsRefresh { get; private set; } = true;
    public void Invalidate() => NeedsRefresh = true;
    public void Publish(T value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        NeedsRefresh = false;
    }
    // Clear only at a session boundary, never on a routine refresh or a network failure.
    public void Clear() { Value = null; NeedsRefresh = true; }
}
