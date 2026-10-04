namespace SelectiveVpnRouter.Core.ApplicationDiscovery;

public static class ApplicationDiscoverySearch
{
    public static IReadOnlyList<DiscoveredApplication> Filter(
        IEnumerable<DiscoveredApplication> applications,
        string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return applications.ToList();
        }

        string term = searchTerm.Trim();
        return applications
            .Where(app => Matches(app, term))
            .ToList();
    }

    internal static bool Matches(DiscoveredApplication app, string term)
    {
        if (app.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(app.Publisher)
            && app.Publisher.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (app.ExecutableFileName.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return app.ExecutablePath.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}