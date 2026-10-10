using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public sealed class UpdateDesktopSmokeOrchestrationTests
{
    private const string PublishRoot = @"C:\dev\artifacts\publish\SelectiveVpnRouter";

    [Fact]
    public void ProductWasRunning_false_does_not_plan_routing_fast_smoke()
    {
        var snap = Snapshot(productRunning: false);
        Assert.False(UpdateDesktopSmokeOrchestrationPlanner.ShouldRunRoutingFastSmoke(snap));
        Assert.False(UpdateDesktopSmokeOrchestrationPlanner.ShouldWaitForServiceIpc(snap));
    }

    [Fact]
    public void ProductWasRunning_true_plans_routing_fast_and_service_ipc_wait()
    {
        var snap = Snapshot(productRunning: true);
        Assert.True(UpdateDesktopSmokeOrchestrationPlanner.ShouldRunRoutingFastSmoke(snap));
        Assert.True(UpdateDesktopSmokeOrchestrationPlanner.ShouldWaitForServiceIpc(snap));
    }

    [Fact]
    public void Update_desktop_skips_routing_fast_when_product_was_not_running()
    {
        string script = ReadUpdateDesktopScript();
        int smokeStart = script.IndexOf("Invoke-SvrStep -Name \"smoke\"", StringComparison.Ordinal);
        Assert.True(smokeStart >= 0);
        int runFlag = script.IndexOf("$runRoutingFastSmoke = $false", StringComparison.Ordinal);
        Assert.True(runFlag >= 0 && runFlag < smokeStart);

        string smokeBlock = ExtractSmokeActionBlock(script, smokeStart);
        Assert.Contains("if ($runRoutingFastSmoke)", smokeBlock, StringComparison.Ordinal);
        Assert.Contains(
            "routing-fast: SKIP reason: product service was stopped before update and stopped state was preserved",
            smokeBlock,
            StringComparison.Ordinal);
        int invokeIdx = smokeBlock.IndexOf("test-routing-fast.ps1", StringComparison.Ordinal);
        int ifIdx = smokeBlock.IndexOf("if ($runRoutingFastSmoke)", StringComparison.Ordinal);
        int elseIdx = smokeBlock.IndexOf("else {", ifIdx, StringComparison.Ordinal);
        Assert.True(ifIdx >= 0 && invokeIdx > ifIdx && invokeIdx < elseIdx);
    }

    [Fact]
    public void Update_desktop_invokes_routing_fast_when_restore_plan_starts_product()
    {
        string script = ReadUpdateDesktopScript();
        string preSmoke = script[..script.IndexOf("Invoke-SvrStep -Name \"smoke\"", StringComparison.Ordinal)];
        Assert.Contains(
            "$runRoutingFastSmoke = $script:deployRuntimeSnapshot.ProductInstalled -and $script:deployRuntimeSnapshot.ProductWasRunning",
            preSmoke,
            StringComparison.Ordinal);

        string smokeBlock = ExtractSmokeActionBlock(script, script.IndexOf("Invoke-SvrStep -Name \"smoke\"", StringComparison.Ordinal));
        Assert.Contains("-AllowedExitCodes @(0, 2)", smokeBlock, StringComparison.Ordinal);
        Assert.Contains("test-routing-fast.ps1", smokeBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_ready_failure_exits_before_smoke_when_product_was_running()
    {
        string script = ReadUpdateDesktopScript();
        int serviceReady = script.IndexOf("=== STEP service-ready ===", StringComparison.Ordinal);
        int smoke = script.IndexOf("Invoke-SvrStep -Name \"smoke\"", StringComparison.Ordinal);
        Assert.True(serviceReady >= 0 && smoke > serviceReady);

        string serviceBlock = script[serviceReady..smoke];
        Assert.Contains("ProductWasRunning", serviceBlock, StringComparison.Ordinal);
        Assert.Contains("exit 1", serviceBlock, StringComparison.Ordinal);
        Assert.Contains("Wait-SvrIpcReady", serviceBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Routing_fast_exit_codes_accept_pass_and_vpn_not_ready_skip_only_when_invoked()
    {
        string script = ReadUpdateDesktopScript();
        string smokeBlock = ExtractSmokeActionBlock(script, script.IndexOf("Invoke-SvrStep -Name \"smoke\"", StringComparison.Ordinal));
        Assert.Contains("if ($runRoutingFastSmoke)", smokeBlock, StringComparison.Ordinal);
        Assert.Contains("-AllowedExitCodes @(0, 2)", smokeBlock, StringComparison.Ordinal);
        Assert.Contains("test-routing-fast.ps1", smokeBlock, StringComparison.Ordinal);
    }

    private static PublishRuntimeSnapshot Snapshot(bool productRunning) =>
        new(
            ProductInstalled: true,
            ProductWasRunning: productRunning,
            ProductImagePath: Path.Combine(PublishRoot, "SelectiveVpnRouter.Service.exe"),
            DriverInstalled: true,
            DriverWasRunning: false,
            DriverImagePath: Path.Combine(PublishRoot, "driver", "SelectiveVpnCallout.sys"));

    private static string ReadUpdateDesktopScript()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "scripts", "update-desktop.ps1");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("update-desktop.ps1 not found");
    }

    private static string ExtractSmokeActionBlock(string script, int smokeStepIndex)
    {
        int actionStart = script.IndexOf("-Action {", smokeStepIndex, StringComparison.Ordinal);
        Assert.True(actionStart >= 0);
        int depth = 0;
        for (int i = actionStart; i < script.Length; i++)
        {
            if (script[i] == '{')
            {
                depth++;
            }
            else if (script[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return script[actionStart..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException("smoke action block not found");
    }
}
