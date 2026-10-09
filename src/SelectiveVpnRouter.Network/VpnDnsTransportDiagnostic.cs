using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SelectiveVpnRouter.Network;

/// <summary>Read-only acceptance diagnostics for production VPN-bound UDP DNS (Slice 8).</summary>
public static class VpnDnsTransportDiagnostic
{
    public const int DefaultTimeoutMs = VpnInterfaceDnsResolver.DefaultTimeoutMs;

    public static async Task<VpnDnsTransportProbeReport> ProbeServerAsync(
        int interfaceIndex,
        IPAddress dnsServer,
        string hostname,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var total = Stopwatch.StartNew();
        var report = new VpnDnsTransportProbeReport
        {
            Server = dnsServer.ToString(),
            InterfaceIndex = interfaceIndex,
            Hostname = hostname,
            TimeoutMs = timeoutMs,
            SocketFamily = AddressFamily.InterNetwork.ToString(),
            IpUnicastIfOptionLevel = "IPPROTO_IP (0)",
            IpUnicastIfOptionName = "IP_UNICAST_IF (31)",
            IpUnicastIfValueHostOrder = interfaceIndex,
            IpUnicastIfValueNetworkOrder = IPAddress.HostToNetworkOrder(interfaceIndex),
            IpUnicastIfValueBytes = BitConverter.ToString(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(interfaceIndex))),
        };

        if (!VpnDnsWireFormat.TryValidateHostname(hostname))
        {
            report.FinalResolutionStatus = VpnDnsResolutionStatus.InvalidHostname.ToString();
            report.TotalElapsedMs = total.ElapsedMilliseconds;
            return report;
        }

        ushort transactionId = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        report.TransactionId = transactionId;
        byte[] query = VpnDnsWireFormat.BuildAQuery(hostname, transactionId);

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var bindSw = Stopwatch.StartNew();
        try
        {
            SocketInterfaceBinder.BindIpv4UnicastIf(socket, interfaceIndex);
            report.BindOk = true;
        }
        catch (SocketException ex)
        {
            report.BindOk = false;
            report.BindSocketError = ex.SocketErrorCode.ToString();
            report.BindWin32Error = ex.NativeErrorCode;
            report.FinalResolutionStatus = VpnDnsResolutionStatus.BindFailed.ToString();
            report.TotalElapsedMs = total.ElapsedMilliseconds;
            report.BindElapsedMs = bindSw.ElapsedMilliseconds;
            return report;
        }

        report.BindElapsedMs = bindSw.ElapsedMilliseconds;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        var sendSw = Stopwatch.StartNew();
        try
        {
            await socket
                .SendToAsync(query, SocketFlags.None, new IPEndPoint(dnsServer, 53), timeout.Token)
                .ConfigureAwait(false);
            report.SendOk = true;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            report.SendOk = false;
            report.SendError = "Cancelled (timeout)";
            report.ReceiveOutcome = VpnDnsTransportOutcome.Cancelled.ToString();
            report.FinalResolutionStatus = VpnDnsResolutionStatus.Timeout.ToString();
            report.TotalElapsedMs = total.ElapsedMilliseconds;
            report.SendElapsedMs = sendSw.ElapsedMilliseconds;
            return report;
        }
        catch (SocketException ex)
        {
            report.SendOk = false;
            report.SendError = ex.SocketErrorCode + " (" + ex.NativeErrorCode + ")";
            report.ReceiveOutcome = VpnDnsTransportOutcome.SendFailed.ToString();
            report.FinalResolutionStatus = VpnDnsResolutionStatus.TransportError.ToString();
            report.TotalElapsedMs = total.ElapsedMilliseconds;
            report.SendElapsedMs = sendSw.ElapsedMilliseconds;
            return report;
        }

        report.SendElapsedMs = sendSw.ElapsedMilliseconds;

        var buffer = new byte[VpnDnsWireFormat.DefaultUdpBufferSize];
        var recvSw = Stopwatch.StartNew();
        try
        {
            int n = await socket
                .ReceiveAsync(buffer, SocketFlags.None, timeout.Token)
                .ConfigureAwait(false);
            report.ReceiveOk = true;
            report.ReceiveByteCount = n;
            report.ReceiveOutcome = VpnDnsTransportOutcome.Received.ToString();
            report.ReceiveElapsedMs = recvSw.ElapsedMilliseconds;

            if (n >= 12)
            {
                ushort flags = (ushort)((buffer[2] << 8) | buffer[3]);
                report.QueryResponse = (flags & 0x8000) != 0;
                report.Rcode = flags & 0x000F;
                report.Tc = (flags & 0x0200) != 0;
                report.ResponseTransactionId = (ushort)((buffer[0] << 8) | buffer[1]);
                report.TxIdMatch = report.ResponseTransactionId == transactionId;
            }

            VpnDnsWireParseResult parsed = VpnDnsWireFormat.TryParseARecords(buffer.AsSpan(0, n), transactionId);
            report.ParseStatus = parsed.Status.ToString();
            report.ParsedARecords = string.Join(",", parsed.Addresses.Select(a => a.ToString()));
            report.FinalResolutionStatus = MapParseToResolution(parsed.Status).ToString();
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            report.ReceiveOk = false;
            report.ReceiveError = "Cancelled (timeout)";
            report.ReceiveOutcome = VpnDnsTransportOutcome.Cancelled.ToString();
            report.FinalResolutionStatus = VpnDnsResolutionStatus.Timeout.ToString();
            report.ReceiveElapsedMs = recvSw.ElapsedMilliseconds;
        }
        catch (SocketException ex)
        {
            report.ReceiveOk = false;
            report.ReceiveError = ex.SocketErrorCode + " (" + ex.NativeErrorCode + ")";
            report.ReceiveOutcome = VpnDnsTransportOutcome.ReceiveFailed.ToString();
            report.FinalResolutionStatus = VpnDnsResolutionStatus.TransportError.ToString();
            report.ReceiveElapsedMs = recvSw.ElapsedMilliseconds;
        }

        report.TotalElapsedMs = total.ElapsedMilliseconds;
        return report;
    }

    public static string FormatReport(VpnDnsTransportProbeReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("server=" + r.Server);
        sb.AppendLine("hostname=" + r.Hostname);
        sb.AppendLine("interfaceIndex=" + r.InterfaceIndex);
        sb.AppendLine("timeoutMs=" + r.TimeoutMs);
        sb.AppendLine("socketFamily=" + r.SocketFamily);
        sb.AppendLine("IP_UNICAST_IF level=" + r.IpUnicastIfOptionLevel + " name=" + r.IpUnicastIfOptionName);
        sb.AppendLine("IP_UNICAST_IF hostOrder=" + r.IpUnicastIfValueHostOrder);
        sb.AppendLine("IP_UNICAST_IF networkOrder=" + r.IpUnicastIfValueNetworkOrder);
        sb.AppendLine("IP_UNICAST_IF bytes=" + r.IpUnicastIfValueBytes);
        sb.AppendLine("bindOk=" + r.BindOk + " bindElapsedMs=" + r.BindElapsedMs
            + (r.BindSocketError is null ? "" : " bindError=" + r.BindSocketError + " win32=" + r.BindWin32Error));
        sb.AppendLine("transactionId=0x" + r.TransactionId.ToString("X4"));
        sb.AppendLine("sendOk=" + r.SendOk + " sendElapsedMs=" + r.SendElapsedMs
            + (r.SendError is null ? "" : " sendError=" + r.SendError));
        sb.AppendLine("receiveOutcome=" + (r.ReceiveOutcome ?? "(none)"));
        sb.AppendLine("receiveOk=" + r.ReceiveOk + " receiveElapsedMs=" + r.ReceiveElapsedMs + " receiveBytes=" + r.ReceiveByteCount
            + (r.ReceiveError is null ? "" : " receiveError=" + r.ReceiveError));
        sb.AppendLine("totalElapsedMs=" + r.TotalElapsedMs);
        if (r.ResponseTransactionId.HasValue)
        {
            sb.AppendLine("responseTxId=0x" + r.ResponseTransactionId.Value.ToString("X4"));
        }

        sb.AppendLine("QR=" + (r.QueryResponse?.ToString() ?? "?"));
        sb.AppendLine("RCODE=" + (r.Rcode?.ToString() ?? "?"));
        sb.AppendLine("TC=" + (r.Tc?.ToString() ?? "?"));
        sb.AppendLine("txIdMatch=" + (r.TxIdMatch?.ToString() ?? "?"));
        sb.AppendLine("parseStatus=" + (r.ParseStatus ?? "(none)"));
        sb.AppendLine("parsedA=" + (r.ParsedARecords ?? "(none)"));
        sb.AppendLine("finalResolutionStatus=" + r.FinalResolutionStatus);
        return sb.ToString().TrimEnd();
    }

    private static VpnDnsResolutionStatus MapParseToResolution(VpnDnsWireParseStatus status) =>
        status switch
        {
            VpnDnsWireParseStatus.Success => VpnDnsResolutionStatus.Success,
            VpnDnsWireParseStatus.NxDomain => VpnDnsResolutionStatus.NxDomain,
            VpnDnsWireParseStatus.ServFail => VpnDnsResolutionStatus.ServFail,
            VpnDnsWireParseStatus.Truncated => VpnDnsResolutionStatus.TruncatedResponse,
            VpnDnsWireParseStatus.TransactionIdMismatch => VpnDnsResolutionStatus.TransactionIdMismatch,
            VpnDnsWireParseStatus.Malformed => VpnDnsResolutionStatus.MalformedResponse,
            VpnDnsWireParseStatus.NoMatchingAnswer => VpnDnsResolutionStatus.NoMatchingAnswer,
            _ => VpnDnsResolutionStatus.TransportError,
        };
}

public sealed class VpnDnsTransportProbeReport
{
    public string Server { get; set; } = "";
    public string Hostname { get; set; } = "";
    public int InterfaceIndex { get; set; }
    public int TimeoutMs { get; set; }
    public string SocketFamily { get; set; } = "";
    public string IpUnicastIfOptionLevel { get; set; } = "";
    public string IpUnicastIfOptionName { get; set; } = "";
    public int IpUnicastIfValueHostOrder { get; set; }
    public int IpUnicastIfValueNetworkOrder { get; set; }
    public string IpUnicastIfValueBytes { get; set; } = "";
    public bool BindOk { get; set; }
    public long BindElapsedMs { get; set; }
    public string? BindSocketError { get; set; }
    public int? BindWin32Error { get; set; }
    public ushort TransactionId { get; set; }
    public bool SendOk { get; set; }
    public long SendElapsedMs { get; set; }
    public string? SendError { get; set; }
    public bool ReceiveOk { get; set; }
    public long ReceiveElapsedMs { get; set; }
    public int ReceiveByteCount { get; set; }
    public string? ReceiveError { get; set; }
    public string? ReceiveOutcome { get; set; }
    public long TotalElapsedMs { get; set; }
    public bool? QueryResponse { get; set; }
    public int? Rcode { get; set; }
    public bool? Tc { get; set; }
    public ushort? ResponseTransactionId { get; set; }
    public bool? TxIdMatch { get; set; }
    public string? ParseStatus { get; set; }
    public string? ParsedARecords { get; set; }
    public string FinalResolutionStatus { get; set; } = "";
}
