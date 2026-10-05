using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;

namespace SelectiveVpnRouter.Core.RoutingTrace;

public static class RoutingTraceExport
{
    private static readonly Regex AuthorizationHeader = new(
        @"(?i)(authorization\s*:\s*)(.*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerToken = new(
        @"(?i)(Bearer\s+)(.+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        string redacted = AuthorizationHeader.Replace(input, m => m.Groups[1].Value + "[REDACTED]");
        return BearerToken.Replace(redacted, "Bearer [REDACTED]");
    }

    public static string ToRedactedJson(RoutingTraceSession session, IReadOnlyList<RoutingTraceEvent> events)
    {
        var payload = new
        {
            session.SessionId,
            session.StartedAt,
            session.StoppedAt,
            session.State,
            Target = new
            {
                session.Target.DisplayName,
                session.Target.PrimaryExecutablePath,
                session.Target.ExpectedRoute,
            },
            session.Options,
            session.Summary,
            Flows = events.Select(e => new
            {
                e.FlowId,
                e.SequenceId,
                e.ProcessId,
                ProcessStartUtc = e.ProcessStartUtcTicks == 0 ? (DateTimeOffset?)null : new DateTimeOffset(e.ProcessStartUtcTicks, TimeSpan.Zero),
                e.ProcessPath,
                e.Protocol,
                e.AddressFamily,
                Local = FormatEndpoint(e.LocalAddress, e.LocalPort),
                Remote = FormatEndpoint(e.RemoteAddress, e.RemotePort),
                e.FirstSeenUtc,
                e.LastSeenUtc,
                e.PreExistingAtTraceStart,
                e.ExpectedRoute,
                e.ObservedRoute,
                e.CoverageReason,
                e.Outcome,
                Evidence = RoutingTraceEvidenceFormatter.ToTokens(e.EvidenceFlags),
                e.DestinationKind,
            }).ToArray(),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string FormatTextReport(RoutingTraceSession session, IReadOnlyList<RoutingTraceEvent> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Routing Trace Report");
        sb.AppendLine($"Target: {session.Target.DisplayName} ({session.Target.PrimaryExecutablePath})");
        sb.AppendLine($"Session: {session.SessionId}");
        sb.AppendLine($"LogicalFlows: {events.Count}");
        sb.AppendLine();

        foreach (RoutingTraceEvent e in events.Take(500))
        {
            sb.AppendLine(
                $"flow={e.FlowId}\n" +
                $"pid={e.ProcessId}\n" +
                $"procStart={FormatProcessStart(e.ProcessStartUtcTicks)}\n" +
                $"{e.Protocol}/{e.AddressFamily}\n" +
                $"local={FormatEndpoint(e.LocalAddress, e.LocalPort)}\n" +
                $"remote={FormatEndpoint(e.RemoteAddress, e.RemotePort)}\n" +
                $"first={e.FirstSeenUtc:O}\n" +
                $"last={e.LastSeenUtc:O}\n" +
                $"preExisting={e.PreExistingAtTraceStart}\n" +
                $"expected={e.ExpectedRoute}\n" +
                $"observed={e.ObservedRoute}\n" +
                $"reason={e.CoverageReason}\n" +
                $"outcome={e.Outcome}\n" +
                $"evidence={RoutingTraceEvidenceFormatter.ToSummary(e.EvidenceFlags)}\n" +
                $"updates={e.UpdateCount}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatProcessStart(long ticks)
        => ticks == 0 ? "-" : new DateTimeOffset(ticks, TimeSpan.Zero).ToString("O");

    private static string FormatEndpoint(string address, int port)
        => string.IsNullOrWhiteSpace(address) || port <= 0 ? "-" : $"{Redact(address)}:{port}";
}
