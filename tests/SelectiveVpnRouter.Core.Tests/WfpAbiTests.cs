using System.Runtime.InteropServices;
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
    public void Svr_status_managed_size_matches_native_x64()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        int managed = CalloutDriverStatusAbiVerification.ManagedStatusBufferSize;
        Assert.Equal(CalloutDriverStatusAbiVerification.ExpectedNativeStatusSizeBytes, managed);
        Assert.Equal(
            CalloutDriverStatusAbiVerification.LegacyIncorrectManagedSizeBytes,
            CalloutDriverStatusAbiVerification.MeasuredLegacyIncorrectManagedSizeBytes);
        Assert.NotEqual(CalloutDriverStatusAbiVerification.LegacyIncorrectManagedSizeBytes, managed);
    }

    [Fact]
    public void Svr_status_field_offsets_match_native_x64()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        IReadOnlyList<CalloutDriverStatusAbiVerification.LayoutMismatch> mismatches =
            CalloutDriverStatusAbiVerification.CompareManagedToNative();
        Assert.Empty(mismatches);
    }

    [Fact]
    public void Svr_status_runtime_app_id_text_is_1024_bytes_in_struct()
    {
        Assert.Equal(512, CalloutDriverStatusAbiVerification.RuntimeAppIdTextCharCount);
        Assert.Equal(1024, CalloutDriverStatusAbiVerification.RuntimeAppIdTextByteLength);
        int textFieldSize = CalloutDriverStatusAbiVerification.ExpectedNativeStatusSizeBytes
            - CalloutDriverStatusAbiVerification.ExpectedFieldOffsets["RuntimeAppIdText"];
        Assert.Equal(1024, textFieldSize);
    }

    [Fact]
    public void Fwp_data_type_values_match_windows_sdk()
    {
        Assert.Equal(12u, WfpConstants.ExpectedByteBlobType);
        Assert.Equal(14u, WfpConstants.ExpectedSecurityDescriptorType);
    }

    [Fact]
    public void Fwp_e_already_exists_matches_sdk_hresult()
    {
        Assert.Equal(WfpConstants.FwpEAlreadyExists, WfpNativeStatus.FwpEAlreadyExists);
    }

    [Fact]
    public void Runtime_capture_condition_guids_match_windows_sdk_fwpmk_literals()
    {
        foreach (WfpGuidVerificationRow row in WfpRuntimeCaptureGuidVerification.CompareManagedConditionGuidsToSdk())
        {
            Assert.True(row.Equal, row.Name + " managed=" + row.ManagedGuid + " sdk=" + row.SdkGuid);
        }

        Assert.NotEqual(WfpConstants.ConditionIpProtocol, WfpConstants.ConditionIpRemotePort);
        Assert.NotEqual(WfpConstants.ConditionIpProtocol, WfpConstants.ConditionIpRemoteAddress);
        Assert.NotEqual(WfpConstants.ConditionIpRemotePort, WfpConstants.ConditionIpRemoteAddress);
    }

    [Fact]
    public void Diagnostic_remote_address_uint32_encodings_for_test_net()
    {
        Assert.Equal("198.51.100.1", WfpRuntimeAppIdCapture.DiagnosticRemoteHost);
        Assert.Equal(new byte[] { 198, 0x33, 0x64, 0x01 }, WfpRuntimeAppIdCapture.DiagnosticRemoteAddressBytes);
        Assert.Equal(0xC6336401u, WfpRuntimeAppIdCapture.DiagnosticRemoteAddressUInt32);
        Assert.Equal(0xC6336401u, WfpRuntimeAppIdCapture.DiagnosticRemoteAddressUInt32NetworkOrder);
        Assert.Equal(0x016433C6u, WfpRuntimeAppIdCapture.DiagnosticRemoteAddressUInt32HostLittleEndian);
    }

    [Theory]
    [InlineData("198.51.100.1", 0xC6336401u)]
    [InlineData("127.0.0.1", 0x7F000001u)]
    [InlineData("1.2.3.4", 0x01020304u)]
    public void ToWfpIpv4AddressUInt32_uses_network_order(string ipv4, uint expected)
    {
        Assert.Equal(expected, WfpIpv4AddressEncoding.ToWfpIpv4AddressUInt32(ipv4));
    }

    [Fact]
    public void Ale_app_id_comparison_detects_unicode_case_code_unit()
    {
        const string fwpm = @"\device\harddiskvolume4\programdata\vpn route wfp tests\" + "\u0422\u0435\u0441\u0442 vpn route\\selectivevpnrouter.probe.exe";
        const string runtime = @"\device\harddiskvolume4\programdata\vpn route wfp tests\" + "\u0442\u0435\u0441\u0442 vpn route\\selectivevpnrouter.probe.exe";
        Assert.False(WfpAleAppIdComparison.EqualsOrdinal(fwpm, runtime));
        Assert.True(WfpAleAppIdComparison.EqualsOrdinalIgnoreCase(fwpm, runtime));
        WfpAleAppIdDiffDetail diff = WfpAleAppIdComparison.Analyze(fwpm, runtime, 200, 200);
        Assert.True(diff.FirstDiffIndex >= 0);
        Assert.Equal('\u0422', diff.FwpmChar);
        Assert.Equal('\u0442', diff.RuntimeChar);
        Assert.Equal(0x0422, diff.FwpmCodePoint);
        Assert.Equal(0x0442, diff.RuntimeCodePoint);
        Assert.Contains("22 04", diff.Utf16LeHexWindow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("42 04", diff.Utf16LeHexWindow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ale_app_id_comparison_real_probe_paths_do_not_throw_on_hex_window()
    {
        const string fwpm = @"\device\harddiskvolume4\programdata\vpn route wfp tests\"
            + "\u0422\u0435\u0441\u0442 vpn route\\selectivevpnrouter.probe.exe";
        const string runtime = @"\device\harddiskvolume4\programdata\vpn route wfp tests\"
            + "\u0442\u0435\u0441\u0442 vpn route\\selectivevpnrouter.probe.exe";
        WfpAleAppIdDiffDetail diff = WfpAleAppIdComparison.Analyze(fwpm, runtime, 200, 200);
        Assert.False(WfpAleAppIdComparison.EqualsOrdinal(fwpm, runtime));
        Assert.True(WfpAleAppIdComparison.EqualsOrdinalIgnoreCase(fwpm, runtime));
        Assert.Equal('\u0422', diff.FwpmChar);
        Assert.Equal('\u0442', diff.RuntimeChar);
        Assert.NotEmpty(diff.Utf16LeHexWindow);
    }

    [Theory]
    [InlineData("Alpha", "alpha", 0)]
    [InlineData("abc", "abd", 2)]
    public void Ale_app_id_comparison_analyze_edge_indices(string fwpm, string runtime, int expectedFirstDiff)
    {
        WfpAleAppIdDiffDetail diff = WfpAleAppIdComparison.Analyze(fwpm, runtime);
        Assert.Equal(expectedFirstDiff, diff.FirstDiffIndex);
        Assert.NotEmpty(diff.Utf16LeHexWindow);
    }

    [Fact]
    public void Ale_app_id_comparison_analyze_different_lengths_does_not_throw()
    {
        const string fwpm = @"\device\harddiskvolume4\programdata\vpn route wfp tests\"
            + "\u0422\u0435\u0441\u0442 vpn route\\selectivevpnrouter.probe.exe";
        const string runtime = @"\device\harddiskvolume4\programdata\vpn route wfp tests\"
            + "\u0442\u0435\u0441\u0442 vpn route\\selectivevpnrouter.probe.exe";
        WfpAleAppIdDiffDetail diff = WfpAleAppIdComparison.Analyze(fwpm, runtime + "extra");
        Assert.True(diff.FirstDiffIndex >= 0);
        Assert.NotEmpty(diff.Utf16LeHexWindow);
    }

    [Fact]
    public void Ale_app_id_comparison_analyze_identical_strings()
    {
        const string value = "same-device-path";
        WfpAleAppIdDiffDetail diff = WfpAleAppIdComparison.Analyze(value, value);
        Assert.Equal(-1, diff.FirstDiffIndex);
        Assert.Equal("(no ordinal difference)", diff.Utf16LeHexWindow);
    }

    [Fact]
    public void Ale_app_id_comparison_analyze_empty_vs_nonempty()
    {
        WfpAleAppIdDiffDetail diff = WfpAleAppIdComparison.Analyze(string.Empty, "x");
        Assert.Equal(0, diff.FirstDiffIndex);
        Assert.Contains("(empty)", diff.Utf16LeHexWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostic_ale_app_id_blob_utf16le_with_terminator()
    {
        byte[] abc = WfpDiagnosticAleAppIdBlob.GetUtf16LeBytesIncludingTerminator("abc");
        Assert.Equal(8, abc.Length);
        Assert.Equal([0x61, 0x00, 0x62, 0x00, 0x63, 0x00, 0x00, 0x00], abc);

        const string cyrillic = "\u0422\u0435\u0441\u0442";
        byte[] cyr = WfpDiagnosticAleAppIdBlob.GetUtf16LeBytesIncludingTerminator(cyrillic);
        Assert.Equal((cyrillic.Length + 1) * 2, cyr.Length);
        Assert.Equal(0x22, cyr[0]);
        Assert.Equal(0x04, cyr[1]);
        Assert.Equal(0x00, cyr[^1]);
        Assert.Equal(0x00, cyr[^2]);
    }

    [Fact]
    public void Diagnostic_ale_app_id_blob_expected_byte_size_for_99_wchar_runtime_sample()
    {
        const string runtimeSample =
            @"\device\harddiskvolume4\programdata\vpn route wfp tests\"
            + "\u0442\u0435\u0441\u0442 vpn route\\selectivevpnrouter.probe.exe";
        Assert.Equal(99, runtimeSample.Length);
        Assert.Equal(200u, WfpDiagnosticAleAppIdBlob.ExpectedByteSizeForCanonicalString(runtimeSample));
    }

    [Fact]
    public void Diagnostic_ale_app_id_manual_lower_matches_runtime_for_known_unicode_sample()
    {
        const string fwpm =
            @"\device\harddiskvolume4\programdata\vpn route wfp tests\"
            + "\u0422\u0435\u0441\u0442 vpn route\\selectivevpnrouter.probe.exe";
        const string runtime =
            @"\device\harddiskvolume4\programdata\vpn route wfp tests\"
            + "\u0442\u0435\u0441\u0442 vpn route\\selectivevpnrouter.probe.exe";
        byte[] manualRuntime = WfpDiagnosticAleAppIdBlob.GetUtf16LeBytesIncludingTerminator(runtime);
        byte[] manualLower = WfpDiagnosticAleAppIdBlob.GetUtf16LeBytesIncludingTerminator(fwpm.ToLowerInvariant());
        Assert.True(WfpDiagnosticAleAppIdBlob.BytesEqual(manualLower, manualRuntime));
        Assert.Equal(runtime, fwpm.ToLowerInvariant(), StringComparer.Ordinal);
    }

    [Fact]
    public void Runtime_capture_condition_struct_layout_matches_abi_probe()
    {
        Assert.Equal(40, Marshal.SizeOf<WfpNativeTypes.FWPM_FILTER_CONDITION0>());
        Assert.Equal(0, (int)Marshal.OffsetOf<WfpNativeTypes.FWPM_FILTER_CONDITION0>("fieldKey"));
        Assert.Equal(16, (int)Marshal.OffsetOf<WfpNativeTypes.FWPM_FILTER_CONDITION0>("matchType"));
        Assert.Equal(24, (int)Marshal.OffsetOf<WfpNativeTypes.FWPM_FILTER_CONDITION0>("conditionValue"));
    }

    [Fact]
    public void Runtime_capture_policy_forbids_separate_engine_and_registration()
    {
        Assert.True(WfpRuntimeCaptureFilterPolicy.RequiresProductionWfpSession);
        Assert.Contains("FwpmEngineOpen0", WfpRuntimeCaptureFilterPolicy.ForbiddenUserModeOperations);
        Assert.Contains("FwpmCalloutAdd0", WfpRuntimeCaptureFilterPolicy.ForbiddenUserModeOperations);
    }

    [Fact]
    public void Runtime_capture_install_uses_production_installer_only()
    {
        var fake = new RecordingRuntimeCaptureInstaller();
        using var capture = new WfpRuntimeAppIdCapture(fake);

        Assert.True(capture.TryInstall(out string error));
        Assert.Equal("", error);
        Assert.True(fake.FilterAddCalled);
        Assert.True(capture.ReusedProductionWfpSession);
        Assert.Equal(9001ul, capture.DiagnosticFilterId);
    }

    [Fact]
    public void Runtime_capture_dispose_removes_only_diagnostic_filter_key()
    {
        var fake = new RecordingRuntimeCaptureInstaller();
        using (var capture = new WfpRuntimeAppIdCapture(fake))
        {
            capture.TryInstall(out _);
        }

        Assert.Equal(1, fake.RemoveCallCount);
        Assert.Equal(fake.LastFilterKey, fake.LastRemovedFilterKey);
    }

    private sealed class RecordingRuntimeCaptureInstaller : IWfpRuntimeCaptureFilterInstaller
    {
        public bool SessionOpen => true;

        public bool FilterAddCalled { get; private set; }

        public Guid LastFilterKey { get; private set; }

        public Guid LastRemovedFilterKey { get; private set; }

        public int RemoveCallCount { get; private set; }

        public bool TryAddRuntimeAppIdCaptureFilter(out WfpRuntimeCaptureFilterResult result)
        {
            FilterAddCalled = true;
            LastFilterKey = Guid.Parse("11111111-1111-1111-1111-111111111111");
            result = new WfpRuntimeCaptureFilterResult(0, 9001, LastFilterKey, null);
            return true;
        }

        public void RemoveRuntimeAppIdCaptureFilter(Guid filterKey)
        {
            RemoveCallCount++;
            LastRemovedFilterKey = filterKey;
        }
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