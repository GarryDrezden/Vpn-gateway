namespace SelectiveVpnRouter.Core.RoutingTrace;

public static class RoutingTraceFlowCorrelation
{
    /// <summary>
    /// Max gap between lifecycle updates for weak-key-only proxy flows (no local endpoint yet).
    /// </summary>
    public static readonly TimeSpan WeakTemporalMergeWindow = TimeSpan.FromMinutes(2);

    public static string BuildStrongKey(
        int pid,
        long processStartUtcTicks,
        RoutingTraceProtocol protocol,
        RoutingTraceAddressFamily addressFamily,
        string localAddress,
        int localPort,
        string remoteAddress,
        int remotePort)
        => string.Join(
            '|',
            "strong",
            pid,
            processStartUtcTicks,
            (int)protocol,
            (int)addressFamily,
            Normalize(localAddress),
            localPort,
            Normalize(remoteAddress),
            remotePort);

    public static string BuildWeakKey(
        int pid,
        long processStartUtcTicks,
        RoutingTraceProtocol protocol,
        RoutingTraceAddressFamily addressFamily,
        string remoteAddress,
        int remotePort)
        => string.Join(
            '|',
            "weak",
            pid,
            processStartUtcTicks,
            (int)protocol,
            (int)addressFamily,
            Normalize(remoteAddress),
            remotePort);

    public static (string PrimaryKey, string WeakKey) FromProxyFlow(FlowEvent flow, long processStartUtcTicks)
    {
        ParseEndpoint(flow.OutboundLocalEndpoint, out string? localAddress, out int localPort);
        string weak = BuildWeakKey(flow.Pid, processStartUtcTicks, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, flow.Destination, flow.Port);
        if (localPort > 0 && !string.IsNullOrWhiteSpace(localAddress))
        {
            return (
                BuildStrongKey(flow.Pid, processStartUtcTicks, RoutingTraceProtocol.Tcp, RoutingTraceAddressFamily.IPv4, localAddress, localPort, flow.Destination, flow.Port),
                weak);
        }

        return (weak, weak);
    }

    public static (string PrimaryKey, string WeakKey) FromPassiveObservation(PassiveSocketObservation obs, long processStartUtcTicks)
    {
        string weak = BuildWeakKey(obs.Pid, processStartUtcTicks, obs.Protocol, obs.AddressFamily, obs.RemoteAddress, obs.RemotePort);
        if (obs.LocalPort > 0
            && !string.IsNullOrWhiteSpace(obs.LocalAddress)
            && obs.LocalAddress is not "0.0.0.0"
            && obs.LocalAddress is not "::")
        {
            return (
                BuildStrongKey(obs.Pid, processStartUtcTicks, obs.Protocol, obs.AddressFamily, obs.LocalAddress, obs.LocalPort, obs.RemoteAddress, obs.RemotePort),
                weak);
        }

        return (weak, weak);
    }

    private static void ParseEndpoint(string? endpoint, out string? address, out int port)
    {
        address = null;
        port = 0;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        int idx = endpoint.LastIndexOf(':');
        if (idx <= 0)
        {
            return;
        }

        address = endpoint[..idx].Trim();
        if (int.TryParse(endpoint[(idx + 1)..], out int parsed))
        {
            port = parsed;
        }
    }

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
