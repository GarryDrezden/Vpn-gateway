namespace SelectiveVpnRouter.Core.RoutingTrace;

public enum RoutingTraceProcessStartSource
{
    Os = 0,
    ConnectionTelemetry = 1,
}

public static class RoutingTraceSourceAliases
{
    public static string ProxyFlow(Guid proxyFlowId) => $"proxy:{proxyFlowId:D}";

    public static bool IsProxyFlowAlias(string key) =>
        key.StartsWith("proxy:", StringComparison.Ordinal);
}
