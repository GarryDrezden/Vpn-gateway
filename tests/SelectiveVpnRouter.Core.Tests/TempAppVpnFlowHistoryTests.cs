using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class TempAppVpnFlowHistoryTests
{
    private static string ChromeExe() => Path.GetFullPath(@"C:\Program Files\Google\Chrome\Application\chrome.exe");
    private static string CurlExe() => Path.GetFullPath(@"C:\Windows\System32\curl.exe");

    private static FlowEvent Flow(
        string exe,
        long sequenceId,
        DateTimeOffset updatedAt,
        string destination,
        int port,
        string status,
        bool fullChain = false,
        string? errorReason = null)
    {
        bool error = FlowStatusHelper.IsError(status) || FlowStatusHelper.IsCancelled(status);
        return new FlowEvent
        {
            FlowId = Guid.NewGuid(),
            SequenceId = sequenceId,
            CreatedAt = updatedAt.AddSeconds(-1),
            UpdatedAt = updatedAt,
            ProcessPath = exe,
            Pid = 1000 + (int)sequenceId,
            Destination = destination,
            Port = port,
            WfpRedirect = fullChain,
            ProxyAccepted = fullChain,
            RedirectRecordsApplied = fullChain,
            VpnOutboundCreated = fullChain,
            VpnOutboundBound = fullChain,
            VpnOutboundConnected = fullChain && !error,
            Route = fullChain ? FlowRoute.Vpn : FlowRoute.Unknown,
            Status = status,
            ErrorDetails = errorReason is null
                ? null
                : new FlowErrorDetails
                {
                    Phase = "connect-original-destination",
                    CancellationReason = errorReason,
                    Message = errorReason,
                },
        };
    }

    [Fact]
    public void Query_filters_exact_exe_path_only()
    {
        string chrome = ChromeExe();
        string curl = CurlExe();
        var flows = new[]
        {
            Flow(chrome, 1, DateTimeOffset.UtcNow, "142.251.1.1", 443, FlowLifecycle.Closed, fullChain: true),
            Flow(curl, 2, DateTimeOffset.UtcNow.AddSeconds(1), "93.184.216.34", 443, FlowLifecycle.Closed, fullChain: true),
        };

        IReadOnlyList<TempAppVpnFlowDto> result = TempAppVpnFlowQuery.Query(flows, chrome, 30);
        Assert.Single(result);
        Assert.Equal(chrome, result[0].ProcessPath);
    }

    [Fact]
    public void Query_respects_max_count()
    {
        string chrome = ChromeExe();
        var flows = Enumerable.Range(1, 40)
            .Select(i => Flow(chrome, i, DateTimeOffset.UtcNow.AddSeconds(i), "10.0.0." + (i % 250), 443, FlowLifecycle.Closed))
            .ToArray();

        IReadOnlyList<TempAppVpnFlowDto> result = TempAppVpnFlowQuery.Query(flows, chrome, 30);
        Assert.Equal(30, result.Count);
    }

    [Fact]
    public void Query_sorts_newest_first()
    {
        string chrome = ChromeExe();
        DateTimeOffset t0 = DateTimeOffset.UtcNow;
        var flows = new[]
        {
            Flow(chrome, 1, t0, "1.1.1.1", 443, FlowLifecycle.Closed),
            Flow(chrome, 2, t0.AddSeconds(5), "2.2.2.2", 443, FlowLifecycle.Closed),
            Flow(chrome, 3, t0.AddSeconds(2), "3.3.3.3", 443, FlowLifecycle.Closed),
        };

        IReadOnlyList<TempAppVpnFlowDto> result = TempAppVpnFlowQuery.Query(flows, chrome, 30);
        Assert.Equal("2.2.2.2", result[0].Destination);
        Assert.Equal("3.3.3.3", result[1].Destination);
        Assert.Equal("1.1.1.1", result[2].Destination);
    }

    [Fact]
    public void Mapper_uses_single_flow_id_for_all_fields()
    {
        var flow = Flow(ChromeExe(), 7, DateTimeOffset.UtcNow, "172.67.74.152", 443, FlowLifecycle.Closed, fullChain: true);
        TempAppVpnFlowDto dto = TempAppVpnFlowMapper.FromFlow(flow);
        Assert.Equal(flow.FlowId, dto.FlowId);
        Assert.Equal(flow.Pid, dto.ProcessId);
        Assert.Equal(flow.Destination, dto.Destination);
        Assert.Equal(flow.Status, dto.Status);
        Assert.True(dto.TcpConnectSuccess);
    }

    [Fact]
    public void Target_matcher_matches_ip_and_port()
    {
        TempAppVpnFlowDto dto = TempAppVpnFlowMapper.FromFlow(
            Flow(ChromeExe(), 1, DateTimeOffset.UtcNow, "172.67.74.152", 443, FlowLifecycle.Connecting, fullChain: true));
        Assert.True(TargetFlowMatcher.MatchesTarget(dto, ["172.67.74.152", "104.26.12.205"], 443));
        Assert.False(TargetFlowMatcher.MatchesTarget(dto, ["172.67.74.152"], 80));
        Assert.False(TargetFlowMatcher.MatchesTarget(dto, ["104.26.12.205"], 443));
    }

    [Fact]
    public void Background_flow_does_not_override_target_result()
    {
        string chrome = ChromeExe();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var flows = new[]
        {
            Flow(chrome, 10, now.AddSeconds(10), "142.251.1.1", 443, FlowLifecycle.Closed, fullChain: true),
            Flow(chrome, 11, now.AddSeconds(5), "172.67.74.152", 443, FlowLifecycle.Closed, fullChain: true),
        }.Select(TempAppVpnFlowMapper.FromFlow).ToArray();

        TempAppVpnFlowDto? target = TargetFlowMatcher.SelectLatestTargetFlow(flows, ["172.67.74.152"], 443);
        Assert.NotNull(target);
        Assert.Equal("172.67.74.152", target!.Destination);
        Assert.Equal(TargetAcceptanceState.ClosedPass, TargetFlowAcceptance.ComputeState(target));
    }

    [Fact]
    public void Target_closed_success_is_target_pass()
    {
        TempAppVpnFlowDto target = TempAppVpnFlowMapper.FromFlow(
            Flow(ChromeExe(), 1, DateTimeOffset.UtcNow, "172.67.74.152", 443, FlowLifecycle.Closed, fullChain: true));
        Assert.Equal(TargetAcceptanceState.ClosedPass, TargetFlowAcceptance.ComputeState(target));
        Assert.True(TargetFlowAcceptance.HasTargetTcpFlowPass(target));
    }

    [Fact]
    public void Background_error_and_target_closed_is_target_pass()
    {
        string chrome = ChromeExe();
        var flows = new[]
        {
            Flow(chrome, 1, DateTimeOffset.UtcNow.AddSeconds(2), "209.85.233.94", 443, FlowLifecycle.Cancelled, fullChain: true, errorReason: "ConnectTimeout"),
            Flow(chrome, 2, DateTimeOffset.UtcNow.AddSeconds(1), "172.67.74.152", 443, FlowLifecycle.Closed, fullChain: true),
        }.Select(TempAppVpnFlowMapper.FromFlow).ToArray();

        TempAppVpnFlowDto? target = TargetFlowMatcher.SelectLatestTargetFlow(flows, ["172.67.74.152"], 443);
        Assert.Equal(TargetAcceptanceState.ClosedPass, TargetFlowAcceptance.ComputeState(target));
    }

    [Fact]
    public void Background_closed_and_target_error_is_target_error()
    {
        string chrome = ChromeExe();
        var flows = new[]
        {
            Flow(chrome, 1, DateTimeOffset.UtcNow.AddSeconds(2), "142.251.1.1", 443, FlowLifecycle.Closed, fullChain: true),
            Flow(chrome, 2, DateTimeOffset.UtcNow.AddSeconds(1), "172.67.74.152", 443, FlowLifecycle.Cancelled, fullChain: true, errorReason: "ConnectTimeout"),
        }.Select(TempAppVpnFlowMapper.FromFlow).ToArray();

        TempAppVpnFlowDto? target = TargetFlowMatcher.SelectLatestTargetFlow(flows, ["172.67.74.152"], 443);
        Assert.Equal(TargetAcceptanceState.Error, TargetFlowAcceptance.ComputeState(target));
    }

    [Fact]
    public void Unrelated_executable_is_excluded()
    {
        string chrome = ChromeExe();
        string curl = CurlExe();
        var flows = new[]
        {
            Flow(curl, 1, DateTimeOffset.UtcNow, "172.67.74.152", 443, FlowLifecycle.Closed, fullChain: true),
        };

        IReadOnlyList<TempAppVpnFlowDto> result = TempAppVpnFlowQuery.Query(flows, chrome, 30);
        Assert.Empty(result);
        Assert.Null(TargetFlowMatcher.SelectLatestTargetFlow(result, ["172.67.74.152"], 443));
        Assert.Equal(TargetAcceptanceState.NotSeen, TargetFlowAcceptance.ComputeState(null));
    }
}