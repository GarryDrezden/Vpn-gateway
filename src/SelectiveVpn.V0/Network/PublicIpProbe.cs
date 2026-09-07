using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace SelectiveVpn.V0.Network;

internal sealed record DirectProbeResult
{
    public required bool Ok { get; init; }
    public string? PublicIp { get; init; }
    public required string Endpoint { get; init; }
    public string? Error { get; init; }
}

internal sealed record BoundSocketProbeResult
{
    public required int InterfaceIndex { get; init; }
    public required string Destination { get; init; }
    public bool OptionSet { get; init; }
    public int? OptionReadBack { get; init; }
    public string? OptionError { get; init; }
    public bool TcpConnected { get; init; }
    public bool TlsOk { get; init; }
    public string? PublicIp { get; init; }
    public int? WinsockError { get; init; }
    public string? Error { get; init; }
}

internal sealed record HttpsIpProbeResult
{
    public required string Destination { get; init; }
    public bool TcpConnected { get; init; }
    public bool TlsOk { get; init; }
    public string? PublicIp { get; init; }
    public int? WinsockError { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Direct probes use HttpClient (system default route). Pinned-IP probes build
/// Socket → NetworkStream → SslStream so the TCP destination is the chosen IPv4.
/// IP_UNICAST_IF remains available as a V0 diagnostic and is not used by V0.1.
/// </summary>
internal static class PublicIpProbe
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    internal static readonly (string Host, string Path)[] Endpoints =
    [
        ("api.ipify.org", "/"),
        ("icanhazip.com", "/"),
        ("ifconfig.me", "/ip"),
    ];

    public static async Task<DirectProbeResult> ProbeDirectAsync(CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach ((string host, string path) in Endpoints)
        {
            string url = $"https://{host}{path}";
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", "SelectiveVpn.V0");
                using HttpResponseMessage response = await Http.SendAsync(req, cancellationToken).ConfigureAwait(false);
                string body = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
                if (response.IsSuccessStatusCode && TryParsePublicIp(body, out string ip))
                {
                    return new DirectProbeResult { Ok = true, PublicIp = ip, Endpoint = url };
                }

                last = new InvalidOperationException($"{url} HTTP {(int)response.StatusCode}: {TrimForLog(body)}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }
        }

        return new DirectProbeResult
        {
            Ok = false,
            Endpoint = string.Join(" then ", Endpoints.Select(e => $"https://{e.Host}{e.Path}")),
            Error = last?.Message ?? "All public-IP endpoints failed.",
        };
    }

    public static async Task<BoundSocketProbeResult> ProbeViaIpv4UnicastIfAsync(
        int ipv4InterfaceIndex,
        CancellationToken cancellationToken)
    {
        Exception? lastDns = null;
        foreach ((string host, string path) in Endpoints)
        {
            IPAddress? ipv4 = null;
            try
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
                ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastDns = ex;
            }

            if (ipv4 is null)
            {
                continue;
            }

            return await ProbeOneAsync(ipv4InterfaceIndex, host, path, ipv4, cancellationToken).ConfigureAwait(false);
        }

        return new BoundSocketProbeResult
        {
            InterfaceIndex = ipv4InterfaceIndex,
            Destination = string.Join(", ", Endpoints.Select(e => e.Host)),
            Error = lastDns?.Message ?? "No IPv4 address resolved for public-IP endpoints.",
        };
    }

    /// <summary>
    /// HTTPS to a pinned IPv4 with SNI/Host of <paramref name="host"/>.
    /// No IP_UNICAST_IF — Windows routing table selects the path.
    /// </summary>
    public static async Task<HttpsIpProbeResult> ProbeHttpsToIpAsync(
        string host,
        string path,
        IPAddress destination,
        CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
            SendTimeout = 15000,
            ReceiveTimeout = 15000,
        };

        return await CompleteHttpsAsync(socket, host, path, destination, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<BoundSocketProbeResult> ProbeOneAsync(
        int ipv4InterfaceIndex,
        string host,
        string path,
        IPAddress destination,
        CancellationToken cancellationToken)
    {
        var result = new BoundSocketProbeResult
        {
            InterfaceIndex = ipv4InterfaceIndex,
            Destination = $"{host} ({destination}:443){path}",
        };

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
            SendTimeout = 15000,
            ReceiveTimeout = 15000,
        };

        try
        {
            WindowsSocketInterfaceBinder.SetIpv4UnicastInterface(socket, ipv4InterfaceIndex);
            result = result with { OptionSet = true };
            if (WindowsSocketInterfaceBinder.TryGetIpv4UnicastInterface(socket, out int readBack))
            {
                result = result with { OptionReadBack = readBack };
            }
        }
        catch (SocketException ex)
        {
            return result with
            {
                OptionSet = false,
                WinsockError = ex.NativeErrorCode,
                OptionError = $"setsockopt IP_UNICAST_IF failed: WSA {ex.NativeErrorCode} {ex.Message}",
                Error = $"setsockopt IP_UNICAST_IF failed: WSA {ex.NativeErrorCode} {ex.Message}",
            };
        }

        HttpsIpProbeResult https = await CompleteHttpsAsync(socket, host, path, destination, cancellationToken)
            .ConfigureAwait(false);
        return result with
        {
            TcpConnected = https.TcpConnected,
            TlsOk = https.TlsOk,
            PublicIp = https.PublicIp,
            WinsockError = https.WinsockError ?? result.WinsockError,
            Error = https.Error,
        };
    }

    private static async Task<HttpsIpProbeResult> CompleteHttpsAsync(
        Socket socket,
        string host,
        string path,
        IPAddress destination,
        CancellationToken cancellationToken)
    {
        var result = new HttpsIpProbeResult
        {
            Destination = $"{host} ({destination}:443){path}",
        };

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(15));
            await socket.ConnectAsync(new IPEndPoint(destination, 443), connectCts.Token).ConfigureAwait(false);
            result = result with { TcpConnected = true };
        }
        catch (Exception ex)
        {
            int? wsa = ex is SocketException se ? se.NativeErrorCode : null;
            string message = ex is OperationCanceledException
                ? "TCP connect timed out."
                : ex.Message;
            return result with
            {
                TcpConnected = false,
                WinsockError = wsa,
                Error = wsa is int code
                    ? $"TCP connect failed: WSA {code} {message}"
                    : $"TCP connect failed: {message}",
            };
        }

        try
        {
            await using NetworkStream network = new(socket, ownsSocket: false);
            await using SslStream tls = new(network, leaveInnerStreamOpen: true);
            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = host,
            };
            using var tlsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            tlsCts.CancelAfter(TimeSpan.FromSeconds(15));
            await tls.AuthenticateAsClientAsync(sslOptions, tlsCts.Token).ConfigureAwait(false);
            result = result with { TlsOk = true };

            string request =
                $"GET {path} HTTP/1.1\r\n" +
                $"Host: {host}\r\n" +
                "User-Agent: SelectiveVpn.V0\r\n" +
                "Accept: text/plain\r\n" +
                "Connection: close\r\n" +
                "\r\n";
            byte[] requestBytes = Encoding.ASCII.GetBytes(request);
            await tls.WriteAsync(requestBytes, cancellationToken).ConfigureAwait(false);
            await tls.FlushAsync(cancellationToken).ConfigureAwait(false);

            using var reader = new StreamReader(tls, Encoding.ASCII);
            string response = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            if (!TryParseHttpBody(response, out string body, out int status) || status is < 200 or >= 300)
            {
                return result with { Error = $"HTTP status {status} from {host}." };
            }

            if (!TryParsePublicIp(body, out string ip))
            {
                return result with { Error = $"Could not parse public IP from {host} body." };
            }

            return result with { PublicIp = ip };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            int? wsa = ex is SocketException se ? se.NativeErrorCode : null;
            return result with
            {
                TlsOk = result.TlsOk,
                WinsockError = wsa,
                Error = wsa is int code ? $"TLS/HTTP failed: WSA {code} {ex.Message}" : $"TLS/HTTP failed: {ex.Message}",
            };
        }
    }

    private static bool TryParseHttpBody(string response, out string body, out int status)
    {
        body = string.Empty;
        status = 0;
        int headerEnd = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd < 0)
        {
            return false;
        }

        string headers = response[..headerEnd];
        body = response[(headerEnd + 4)..];
        string first = headers.Split('\n')[0];
        string[] parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out status))
        {
            return false;
        }

        return true;
    }

    private static bool TryParsePublicIp(string text, out string ip)
    {
        string trimmed = text.Trim();
        if (IPAddress.TryParse(trimmed, out IPAddress? parsed)
            && parsed.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
        {
            ip = parsed.ToString();
            return true;
        }

        var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"\b(\d{1,3}\.){3}\d{1,3}\b");
        if (match.Success && IPAddress.TryParse(match.Value, out parsed))
        {
            ip = parsed.ToString();
            return true;
        }

        ip = string.Empty;
        return false;
    }

    private static string TrimForLog(string body)
        => body.Length <= 80 ? body : body[..80] + "...";
}
