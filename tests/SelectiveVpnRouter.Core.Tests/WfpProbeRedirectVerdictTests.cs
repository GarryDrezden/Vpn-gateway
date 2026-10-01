using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WfpProbeRedirectVerdictTests
{
    [Fact]
    public void Unicode_single_long_filter_meets_expectation_without_short_fallback()
    {
        var filters = new[]
        {
            new WfpFilterInstallResult
            {
                FilterInstalled = true,
                AppIdResolved = true,
                IsCalloutFilter = true,
                IsShortPathFallback = false,
                IdentityPathUsed = @"C:\ProgramData\VPN Route WFP Tests\Тест VPN Route\SelectiveVpnRouter.Probe.exe",
            },
        };

        Assert.True(WfpProbeRedirectDiagnosticVerdict.InstalledFiltersMeetExpectation(
            WfpAppIdentityPathMode.Default,
            filters[0].IdentityPathUsed!,
            filters));
    }

    [Fact]
    public void Regression_unicode_redirect_report_shape_is_pass()
    {
        bool pass = WfpProbeRedirectDiagnosticVerdict.EndToEndRoutingSucceeded(
            filtersOk: true,
            httpOk: true,
            calloutMatched: true,
            applyModified: true,
            proxyAccepted: true,
            redirectContextRecovered: true,
            proxyFlowObserved: true,
            localEndpoint: "192.168.0.10:54321",
            publicIp: "91.184.250.53",
            launchError: null,
            infrastructureFailure: null,
            baselineDirectPublicIp: "203.0.113.9");

        Assert.True(pass);
    }

    [Fact]
    public void Legacy_dual_filter_requirement_would_fail_without_long_filter()
    {
        var filters = Array.Empty<WfpFilterInstallResult>();
        Assert.False(WfpProbeRedirectDiagnosticVerdict.InstalledFiltersMeetExpectation(
            WfpAppIdentityPathMode.Default,
            @"C:\path\probe.exe",
            filters));
    }
    [Fact]
    public void Only_short_fallback_filter_fails_default_mode()
    {
        var filters = new[]
        {
            new WfpFilterInstallResult
            {
                FilterInstalled = true,
                AppIdResolved = true,
                IsCalloutFilter = true,
                IsShortPathFallback = true,
            },
        };

        Assert.False(WfpProbeRedirectDiagnosticVerdict.InstalledFiltersMeetExpectation(
            WfpAppIdentityPathMode.Default,
            @"C:\path\probe.exe",
            filters));
    }

    [Fact]
    public void EndToEnd_fails_when_legacy_filtersOk_false_but_other_telemetry_true()
    {
        Assert.False(WfpProbeRedirectDiagnosticVerdict.EndToEndRoutingSucceeded(
            filtersOk: false,
            httpOk: true,
            calloutMatched: true,
            applyModified: true,
            proxyAccepted: true,
            redirectContextRecovered: true,
            proxyFlowObserved: true,
            localEndpoint: "192.168.0.10:54321",
            publicIp: "91.184.250.53",
            launchError: null,
            infrastructureFailure: null,
            baselineDirectPublicIp: "203.0.113.9"));
    }
}