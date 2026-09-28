using SelectiveVpnRouter.Network;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WfpAbiTests
{
    [Fact]
    public void Managed_wfp_struct_layout_matches_windows_sdk_probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        IReadOnlyList<WfpAbiVerification.LayoutMismatch> mismatches = WfpAbiVerification.CompareManagedToNative();
        Assert.Empty(mismatches);
    }

    [Fact]
    public void Fwp_data_type_values_match_windows_sdk()
    {
        Assert.Equal(12u, WfpConstants.ExpectedByteBlobType);
        Assert.Equal(14u, WfpConstants.ExpectedSecurityDescriptorType);
    }

    [Fact]
    public void Fwpm_condition_ale_app_id_matches_sdk_constant()
    {
        Guid expected = new("d78e1e87-8644-4ea5-9437-d809ecefc971");
        Assert.Equal(expected, WfpConstants.ConditionAleAppId);
        Assert.NotEqual(WfpConstants.ConditionAleUserId, WfpConstants.ConditionAleAppId);
    }

    [Fact]
    public void DescribeStatus_maps_invalid_security_descr()
    {
        string text = WfpPolicyHealth.DescribeStatus(0x0000053A);
        Assert.Contains("ERROR_INVALID_SECURITY_DESCR", text);
    }

    [Fact]
    public void Managed_appid_smoke_reproduces_production_condition()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!IsElevated())
        {
            return;
        }

        WfpAppIdSmokeResult result = WfpAppIdSmokeTest.Run();
        Assert.True(result.Success, result.Error + " " + result.DiagnosticLine);
        Assert.True(result.FilterId > 0);
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}