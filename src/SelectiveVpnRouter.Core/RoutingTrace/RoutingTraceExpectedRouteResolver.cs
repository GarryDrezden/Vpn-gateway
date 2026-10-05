namespace SelectiveVpnRouter.Core.RoutingTrace;

internal static class RoutingTraceExpectedRouteResolver
{
    public static RoutingTraceExpectedRoute Resolve(
        RoutingTraceTarget target,
        RoutingTraceAttribution attribution,
        RoutingTraceDestinationKind destinationKind)
    {
        if (destinationKind == RoutingTraceDestinationKind.Loopback)
        {
            return RoutingTraceExpectedRoute.Local;
        }

        if (!attribution.InheritsTargetExpectedRoute)
        {
            return RoutingTraceExpectedRoute.DefaultDirect;
        }

        return target.ExpectedRoute;
    }
}
