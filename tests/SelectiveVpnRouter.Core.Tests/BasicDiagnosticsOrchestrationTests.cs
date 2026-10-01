using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class BasicDiagnosticsOrchestrationTests
{
    [Fact]
    public void Basic_diagnostics_steps_exclude_destructive_checks()
    {
        Assert.True(BasicDiagnosticsOrchestration.UsesOnlySafeSteps(BasicDiagnosticsOrchestration.SafeSteps));
    }
}