namespace SelectiveVpnRouter.Core;

public static class FlowPresentationHelper
{
    public const int DefaultUserFlowLimit = 200;

    public static bool IsUserVisibleFlow(FlowEvent flow)
    {
        if (string.IsNullOrWhiteSpace(flow.ProcessPath))
        {
            return false;
        }

        if (flow.ProcessPath.Contains("SelectiveVpnRouter.Service", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ApplicationRulesHelper.IsDiagnosticExecutablePath(flow.ProcessPath))
        {
            return false;
        }

        return true;
    }

    public static IEnumerable<FlowEvent> SelectUserFlows(
        IEnumerable<FlowEvent> flows,
        int maxCount = DefaultUserFlowLimit)
    {
        return flows
            .Where(IsUserVisibleFlow)
            .OrderByDescending(f => f.UpdatedAt)
            .Take(maxCount);
    }

    public static IEnumerable<FlowEvent> FilterUserFlows(
        IEnumerable<FlowEvent> flows,
        string? search,
        FlowRoute? routeFilter,
        bool errorsOnly,
        string? applicationFilter,
        int maxCount = DefaultUserFlowLimit)
    {
        IEnumerable<FlowEvent> query = SelectUserFlows(flows, maxCount);
        if (routeFilter is FlowRoute route)
        {
            query = query.Where(f => f.Route == route);
        }

        if (errorsOnly)
        {
            query = query.Where(f => FlowStatusHelper.IsError(f.Status) || FlowStatusHelper.IsCancelled(f.Status));
        }

        if (!string.IsNullOrWhiteSpace(applicationFilter) && applicationFilter != "*")
        {
            query = query.Where(f =>
                string.Equals(Path.GetFileName(f.ProcessPath), applicationFilter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.RuleName, applicationFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        string term = search.Trim();
        return query.Where(f =>
            (f.RuleName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || f.ProcessPath.Contains(term, StringComparison.OrdinalIgnoreCase)
            || f.Destination.Contains(term, StringComparison.OrdinalIgnoreCase)
            || f.Status.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}