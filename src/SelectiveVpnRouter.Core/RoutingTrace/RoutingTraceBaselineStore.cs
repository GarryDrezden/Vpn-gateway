namespace SelectiveVpnRouter.Core.RoutingTrace;

public sealed class RoutingTraceBaselineStore
{
    private readonly HashSet<string> _strongKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _weakKeys = new(StringComparer.Ordinal);

    public void Add(string primaryKey, string weakKey)
    {
        if (!string.IsNullOrWhiteSpace(primaryKey))
        {
            _strongKeys.Add(primaryKey);
        }

        if (!string.IsNullOrWhiteSpace(weakKey))
        {
            _weakKeys.Add(weakKey);
        }
    }

    public bool IsPreExisting(string primaryKey, string weakKey)
    {
        if (_strongKeys.Contains(primaryKey))
        {
            return true;
        }

        if (string.Equals(primaryKey, weakKey, StringComparison.Ordinal) && _weakKeys.Contains(weakKey))
        {
            return true;
        }

        return false;
    }

    public void Clear()
    {
        _strongKeys.Clear();
        _weakKeys.Clear();
    }
}
