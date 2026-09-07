using System.Net;
using System.Net.Sockets;

namespace SelectiveVpn.V0.Network;

internal sealed record ResolvedTestTarget(string Host, string Path, IPAddress Ipv4);

internal static class TestTargetResolver
{
    public static async Task<ResolvedTestTarget?> ResolveWorkingAsync(CancellationToken cancellationToken)
    {
        foreach ((string host, string path) in PublicIpProbe.Endpoints)
        {
            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"  DNS {host}: {ex.Message}");
                continue;
            }

            foreach (IPAddress ipv4 in addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork))
            {
                Console.WriteLine($"  Trying {host} at {ipv4} ...");
                HttpsIpProbeResult probe = await PublicIpProbe
                    .ProbeHttpsToIpAsync(host, path, ipv4, cancellationToken)
                    .ConfigureAwait(false);
                if (probe.TcpConnected && probe.TlsOk && probe.PublicIp is not null)
                {
                    return new ResolvedTestTarget(host, path, ipv4);
                }

                Console.WriteLine("    " + (probe.Error ?? "not usable"));
            }
        }

        return null;
    }

    public static async Task<ResolvedTestTarget?> ResolveControlAsync(
        ResolvedTestTarget test,
        CancellationToken cancellationToken)
    {
        foreach ((string host, string path) in PublicIpProbe.Endpoints)
        {
            if (string.Equals(host, test.Host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                continue;
            }

            IPAddress? ipv4 = addresses.FirstOrDefault(a =>
                a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(test.Ipv4));
            if (ipv4 is not null)
            {
                return new ResolvedTestTarget(host, path, ipv4);
            }
        }

        return null;
    }
}
