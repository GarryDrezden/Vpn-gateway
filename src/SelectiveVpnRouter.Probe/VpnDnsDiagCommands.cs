using System.Diagnostics;
using System.Net;
using SelectiveVpnRouter.Core.BrowserRouting;
using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Probe;

internal static class VpnDnsDiagCommands
{
    internal static async Task<int> RunUdpProbeAsync(string[] args)
    {
        if (!TryParseCommon(args, out int ifIndex, out string hostname, out int timeoutMs, out string? serverText))
        {
            Console.WriteLine("usage: --diag-vpn-dns-udp --if-index N --hostname HOST --server IP [--timeout-ms MS]");
            return 2;
        }

        if (!IPAddress.TryParse(serverText, out IPAddress? server) || server.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            Console.WriteLine("invalid --server IPv4: " + serverText);
            return 2;
        }

        VpnDnsTransportProbeReport report = await VpnDnsTransportDiagnostic
            .ProbeServerAsync(ifIndex, server, hostname, timeoutMs)
            .ConfigureAwait(false);
        Console.WriteLine(VpnDnsTransportDiagnostic.FormatReport(report));
        return report.FinalResolutionStatus == nameof(VpnDnsResolutionStatus.Success) ? 0 : 1;
    }

    internal static async Task<int> RunResolverProbeAsync(string[] args)
    {
        if (!TryParseCommon(args, out int ifIndex, out string hostname, out int timeoutMs, out _)
            || !TryGetArg(args, "--servers", out string? serversCsv))
        {
            Console.WriteLine("usage: --diag-vpn-dns-resolver --if-index N --hostname HOST --servers IP,IP,... [--timeout-ms MS]");
            return 2;
        }

        var servers = new List<IPAddress>();
        foreach (string part in serversCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPAddress.TryParse(part, out IPAddress? ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                servers.Add(ip);
        }

        if (servers.Count == 0)
        {
            Console.WriteLine("no IPv4 servers in --servers");
            return 2;
        }

        var tunnel = new DiagTunnel(ifIndex);
        var session = new DiagSessionDns(servers);
        var transport = new VpnBoundUdpDnsTransport();
        var resolver = new VpnInterfaceDnsResolver(tunnel, session, new NetworkInterfaceVpnAdapterDnsServers(), transport, timeoutMs);
        var sw = Stopwatch.StartNew();
        VpnDnsResolutionResult result = await resolver.ResolveIpv4Async(hostname).ConfigureAwait(false);
        sw.Stop();
        Console.WriteLine("resolverElapsedMs=" + sw.ElapsedMilliseconds);
        Console.WriteLine("resolverStatus=" + result.Status);
        Console.WriteLine("resolverSuccess=" + result.IsSuccess);
        Console.WriteLine("resolverAddresses=" + string.Join(",", result.Addresses.Select(a => a.ToString())));
        Console.WriteLine("sessionDnsOrder=" + string.Join(",", servers.Select(a => a.ToString())));
        Console.WriteLine("note=production VpnInterfaceDnsResolver + VpnBoundUdpDnsTransport; timeoutMs=" + timeoutMs + " per server attempt");
        return result.IsSuccess ? 0 : 1;
    }

    private static bool TryParseCommon(
        string[] args,
        out int ifIndex,
        out string hostname,
        out int timeoutMs,
        out string? server)
    {
        ifIndex = 0;
        hostname = "";
        timeoutMs = VpnDnsTransportDiagnostic.DefaultTimeoutMs;
        server = null;
        if (!TryGetArg(args, "--if-index", out string? ifText) || !int.TryParse(ifText, out ifIndex) || ifIndex <= 0)
            return false;
        if (!TryGetArg(args, "--hostname", out hostname) || string.IsNullOrWhiteSpace(hostname))
            return false;
        if (TryGetArg(args, "--timeout-ms", out string? t) && int.TryParse(t, out int parsed) && parsed > 0)
            timeoutMs = parsed;
        TryGetArg(args, "--server", out server);
        return true;
    }

    private static bool TryGetArg(string[] args, string name, out string? value)
    {
        value = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                value = args[i + 1];
                return true;
            }
        }

        return false;
    }

    private sealed class DiagTunnel(int ifIndex) : IVpnTunnelEgressReadiness
    {
        public bool TryGetTunnelInterfaceIndex(out int interfaceIndex)
        {
            interfaceIndex = ifIndex;
            return true;
        }
    }

    private sealed class DiagSessionDns(IReadOnlyList<IPAddress> servers) : IVpnSessionDnsServers
    {
        public IReadOnlyList<IPAddress> GetIpv4DnsServers() => servers;
    }
}
