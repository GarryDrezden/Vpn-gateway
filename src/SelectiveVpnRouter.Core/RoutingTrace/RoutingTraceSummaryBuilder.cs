namespace SelectiveVpnRouter.Core.RoutingTrace;

public static class RoutingTraceSummaryBuilder
{
    public static RoutingTraceSummary Build(
        RoutingTraceTarget target,
        TimeSpan duration,
        IReadOnlyList<RoutingTraceEvent> events)
    {
        var tcp4 = new RoutingTraceProtocolQualityLine { Label = "TCP/IPv4" };
        var udp4 = new RoutingTraceProtocolQualityLine { Label = "UDP/IPv4" };
        var tcp6 = new RoutingTraceProtocolQualityLine { Label = "TCP/IPv6" };
        var udp6 = new RoutingTraceProtocolQualityLine { Label = "UDP/IPv6" };

        int leaks = 0;
        int unknown = 0;
        int uncovered = 0;
        var findingCounts = new Dictionary<RoutingTraceFindingKind, int>();

        foreach (RoutingTraceEvent e in events)
        {
            RoutingTraceProtocolQualityLine line = SelectLine(tcp4, udp4, tcp6, udp6, e);
            line = IncrementLine(line, e);
            AssignLine(e, ref tcp4, ref udp4, ref tcp6, ref udp6, line);

            if (RoutingTraceObservedRouteClassifier.IsConfirmedLeak(e.ExpectedRoute, e.ObservedRoute, e.DestinationKind))
            {
                leaks++;
                AddFinding(findingCounts, RoutingTraceFindingKind.RoutingLeak);
            }

            if (e.ObservedRoute == RoutingTraceObservedRoute.Unknown
                && e.CoverageReason != RoutingTraceCoverageReason.PreExistingAtTraceStart)
            {
                unknown++;
            }

            if (e.ObservedRoute == RoutingTraceObservedRoute.Uncovered)
            {
                uncovered++;
                if (e.Protocol == RoutingTraceProtocol.Udp)
                {
                    AddFinding(findingCounts, RoutingTraceFindingKind.UncoveredProtocol);
                }

                if (e.AddressFamily == RoutingTraceAddressFamily.IPv6)
                {
                    AddFinding(findingCounts, RoutingTraceFindingKind.UncoveredAddressFamily);
                }
            }

            if (e.ExpectedRoute == RoutingTraceExpectedRoute.DefaultDirect
                && e.DestinationKind == RoutingTraceDestinationKind.External)
            {
                AddFinding(findingCounts, RoutingTraceFindingKind.UnassociatedProcess);
            }

            if (e.ExpectedRoute == RoutingTraceExpectedRoute.Local
                && e.ObservedRoute == RoutingTraceObservedRoute.Local)
            {
                AddFinding(findingCounts, RoutingTraceFindingKind.LoopbackCorrect);
            }

            if (e.Outcome == RoutingTraceOutcome.Failed)
            {
                AddFinding(findingCounts, RoutingTraceFindingKind.ConnectionFailure);
            }

            if (e.CoverageReason == RoutingTraceCoverageReason.PreExistingAtTraceStart)
            {
                AddFinding(findingCounts, RoutingTraceFindingKind.PreExistingHistorical);
            }

            if (e.CoverageReason == RoutingTraceCoverageReason.MissingProxyEvidence)
            {
                AddFinding(findingCounts, RoutingTraceFindingKind.MissingProxyEvidence);
            }

            if (e.CoverageReason == RoutingTraceCoverageReason.InsufficientEvidence)
            {
                AddFinding(findingCounts, RoutingTraceFindingKind.UnknownEvidence);
            }
        }

        var findings = findingCounts
            .Select(kv => new RoutingTraceFinding
            {
                Kind = kv.Key,
                Count = kv.Value,
                Summary = DescribeFinding(kv.Key),
            })
            .OrderBy(f => f.Kind)
            .ToList();

        return new RoutingTraceSummary
        {
            Duration = duration,
            TotalEvents = events.Count,
            TotalLogicalFlows = events.Select(e => e.FlowId).Distinct().Count(),
            Quality = new RoutingTraceQualitySummary
            {
                UnknownFlows = unknown,
                ConfirmedLeaks = leaks,
                UncoveredFlows = uncovered,
            },
            ProtocolLines = [tcp4, udp4, tcp6, udp6],
            Findings = findings,
        };
    }

    public static RoutingTraceLiveCounters BuildLiveCounters(IReadOnlyList<RoutingTraceEvent> events)
    {
        int tcp4Vpn = 0;
        int udp4Uncovered = 0;
        int tcp6Uncovered = 0;
        int udp6Uncovered = 0;
        int leaks = 0;
        int tcp4Historical = 0;
        int tcp4MissingProxy = 0;

        foreach (RoutingTraceEvent e in events)
        {
            if (RoutingTraceObservedRouteClassifier.IsConfirmedLeak(e.ExpectedRoute, e.ObservedRoute, e.DestinationKind))
            {
                leaks++;
            }

            if (e.Protocol == RoutingTraceProtocol.Tcp && e.AddressFamily == RoutingTraceAddressFamily.IPv4 && e.ObservedRoute == RoutingTraceObservedRoute.Vpn)
            {
                tcp4Vpn++;
            }

            if (e.Protocol == RoutingTraceProtocol.Tcp && e.AddressFamily == RoutingTraceAddressFamily.IPv4 && e.CoverageReason == RoutingTraceCoverageReason.PreExistingAtTraceStart)
            {
                tcp4Historical++;
            }

            if (e.Protocol == RoutingTraceProtocol.Tcp && e.AddressFamily == RoutingTraceAddressFamily.IPv4 && e.CoverageReason == RoutingTraceCoverageReason.MissingProxyEvidence)
            {
                tcp4MissingProxy++;
            }

            if (e.Protocol == RoutingTraceProtocol.Udp && e.AddressFamily == RoutingTraceAddressFamily.IPv4 && e.ObservedRoute == RoutingTraceObservedRoute.Uncovered)
            {
                udp4Uncovered++;
            }

            if (e.Protocol == RoutingTraceProtocol.Tcp && e.AddressFamily == RoutingTraceAddressFamily.IPv6 && e.ObservedRoute == RoutingTraceObservedRoute.Uncovered)
            {
                tcp6Uncovered++;
            }

            if (e.Protocol == RoutingTraceProtocol.Udp && e.AddressFamily == RoutingTraceAddressFamily.IPv6 && e.ObservedRoute == RoutingTraceObservedRoute.Uncovered)
            {
                udp6Uncovered++;
            }
        }

        return new RoutingTraceLiveCounters
        {
            TcpIpv4Vpn = tcp4Vpn,
            UdpIpv4Uncovered = udp4Uncovered,
            TcpIpv6Uncovered = tcp6Uncovered,
            UdpIpv6Uncovered = udp6Uncovered,
            ConfirmedLeaks = leaks,
            TcpIpv4Historical = tcp4Historical,
            TcpIpv4MissingProxy = tcp4MissingProxy,
        };
    }

    private static RoutingTraceProtocolQualityLine SelectLine(
        RoutingTraceProtocolQualityLine tcp4,
        RoutingTraceProtocolQualityLine udp4,
        RoutingTraceProtocolQualityLine tcp6,
        RoutingTraceProtocolQualityLine udp6,
        RoutingTraceEvent e)
    {
        if (e.Protocol == RoutingTraceProtocol.Tcp && e.AddressFamily == RoutingTraceAddressFamily.IPv4)
        {
            return tcp4;
        }

        if (e.Protocol == RoutingTraceProtocol.Udp && e.AddressFamily == RoutingTraceAddressFamily.IPv4)
        {
            return udp4;
        }

        if (e.Protocol == RoutingTraceProtocol.Tcp && e.AddressFamily == RoutingTraceAddressFamily.IPv6)
        {
            return tcp6;
        }

        return udp6;
    }

    private static void AssignLine(
        RoutingTraceEvent e,
        ref RoutingTraceProtocolQualityLine tcp4,
        ref RoutingTraceProtocolQualityLine udp4,
        ref RoutingTraceProtocolQualityLine tcp6,
        ref RoutingTraceProtocolQualityLine udp6,
        RoutingTraceProtocolQualityLine line)
    {
        if (e.Protocol == RoutingTraceProtocol.Tcp && e.AddressFamily == RoutingTraceAddressFamily.IPv4)
        {
            tcp4 = line;
        }
        else if (e.Protocol == RoutingTraceProtocol.Udp && e.AddressFamily == RoutingTraceAddressFamily.IPv4)
        {
            udp4 = line;
        }
        else if (e.Protocol == RoutingTraceProtocol.Tcp && e.AddressFamily == RoutingTraceAddressFamily.IPv6)
        {
            tcp6 = line;
        }
        else
        {
            udp6 = line;
        }
    }

    private static RoutingTraceProtocolQualityLine IncrementLine(
        RoutingTraceProtocolQualityLine line,
        RoutingTraceEvent e)
    {
        if (e.CoverageReason == RoutingTraceCoverageReason.PreExistingAtTraceStart)
        {
            return line with { Historical = line.Historical + 1 };
        }

        return e.ObservedRoute switch
        {
            RoutingTraceObservedRoute.Vpn => line with { Vpn = line.Vpn + 1 },
            RoutingTraceObservedRoute.Direct => line with { Direct = line.Direct + 1 },
            RoutingTraceObservedRoute.Uncovered => line with { Uncovered = line.Uncovered + 1 },
            RoutingTraceObservedRoute.Local => line with { Local = line.Local + 1 },
            _ => line with { Unknown = line.Unknown + 1 },
        };
    }

    private static void AddFinding(Dictionary<RoutingTraceFindingKind, int> map, RoutingTraceFindingKind kind)
    {
        map[kind] = map.TryGetValue(kind, out int c) ? c + 1 : 1;
    }

    private static string DescribeFinding(RoutingTraceFindingKind kind) => kind switch
    {
        RoutingTraceFindingKind.RoutingLeak => "Confirmed VPN leak (direct egress while VPN expected).",
        RoutingTraceFindingKind.UncoveredProtocol => "UDP flows cannot be proven via TCP proxy path.",
        RoutingTraceFindingKind.UncoveredAddressFamily => "IPv6 flows are not covered by TCP/IPv4 redirect evidence.",
        RoutingTraceFindingKind.UnassociatedProcess => "Process activity outside the traced target scope.",
        RoutingTraceFindingKind.LoopbackCorrect => "Loopback traffic classified as local.",
        RoutingTraceFindingKind.ConnectionFailure => "Connection failures observed during trace.",
        RoutingTraceFindingKind.UnknownEvidence => "Insufficient evidence to classify route.",
        RoutingTraceFindingKind.PreExistingHistorical => "Pre-existing connections detected before trace start; routing evidence may be incomplete.",
        RoutingTraceFindingKind.MissingProxyEvidence => "TCP/IPv4 flow created during trace has no proxy/WFP routing evidence.",
        _ => kind.ToString(),
    };
}
