using System.Runtime.InteropServices;

namespace SelectiveVpnRouter.Network;

/// <summary>
/// Compares managed WFP struct layout with values from native SDK probe (tools/wfp-abi-probe/wfp_abi_probe.exe).
/// </summary>
public static class WfpAbiVerification
{
    public sealed record LayoutExpectation(string Name, int ExpectedSize, IReadOnlyDictionary<string, int>? Offsets = null);

    public static IReadOnlyList<LayoutExpectation> NativeExpectations { get; } =
    [
        new("FWP_VALUE0", 16, new Dictionary<string, int> { ["type"] = 0 }),
        new("FWP_CONDITION_VALUE0", 16, new Dictionary<string, int> { ["type"] = 0 }),
        new("FWP_BYTE_BLOB", 16, new Dictionary<string, int> { ["size"] = 0, ["data"] = 8 }),
        new("FWPM_ACTION0", 20, new Dictionary<string, int> { ["type"] = 0 }),
        new("FWPM_FILTER_CONDITION0", 40, new Dictionary<string, int>
        {
            ["fieldKey"] = 0,
            ["matchType"] = 16,
            ["conditionValue"] = 24,
        }),
        new("FWPM_FILTER0", 200, new Dictionary<string, int>
        {
            ["filterKey"] = 0,
            ["displayData"] = 16,
            ["flags"] = 32,
            ["providerKey"] = 40,
            ["providerData"] = 48,
            ["layerKey"] = 64,
            ["subLayerKey"] = 80,
            ["weight"] = 96,
            ["numFilterConditions"] = 112,
            ["filterCondition"] = 120,
            ["action"] = 128,
            ["context"] = 152,
            ["reserved"] = 168,
            ["filterId"] = 176,
            ["effectiveWeight"] = 184,
        }),
    ];

    public sealed record LayoutMismatch(string Struct, string Field, int Expected, int Actual);

    public static IReadOnlyList<LayoutMismatch> CompareManagedToNative()
    {
        var mismatches = new List<LayoutMismatch>();

        void Check<T>(string name, IReadOnlyDictionary<string, int>? offsets = null) where T : struct
        {
            LayoutExpectation? expected = NativeExpectations.FirstOrDefault(e => e.Name == name);
            if (expected is null)
            {
                return;
            }

            int actualSize = Marshal.SizeOf<T>();
            if (actualSize != expected.ExpectedSize)
            {
                mismatches.Add(new LayoutMismatch(name, "(sizeof)", expected.ExpectedSize, actualSize));
            }

            if (offsets is null)
            {
                return;
            }

            foreach ((string field, int expectedOffset) in offsets)
            {
                int actualOffset = (int)Marshal.OffsetOf<T>(field);
                if (actualOffset != expectedOffset)
                {
                    mismatches.Add(new LayoutMismatch(name, field, expectedOffset, actualOffset));
                }
            }
        }

        Check<WfpNativeTypes.FWP_VALUE0>("FWP_VALUE0", NativeExpectations[0].Offsets);
        Check<WfpNativeTypes.FWP_CONDITION_VALUE0>("FWP_CONDITION_VALUE0", NativeExpectations[1].Offsets);
        Check<WfpNativeTypes.FWP_BYTE_BLOB>("FWP_BYTE_BLOB", NativeExpectations[2].Offsets);
        Check<WfpNativeTypes.FWPM_ACTION0>("FWPM_ACTION0", NativeExpectations[3].Offsets);
        Check<WfpNativeTypes.FWPM_FILTER_CONDITION0>("FWPM_FILTER_CONDITION0", NativeExpectations[4].Offsets);
        Check<WfpNativeTypes.FWPM_FILTER0>("FWPM_FILTER0", NativeExpectations[5].Offsets);

        return mismatches;
    }

    public static void ThrowIfMismatch()
    {
        IReadOnlyList<LayoutMismatch> mismatches = CompareManagedToNative();
        if (mismatches.Count == 0)
        {
            return;
        }

        string details = string.Join("; ", mismatches.Select(m => $"{m.Struct}.{m.Field}: expected {m.Expected}, got {m.Actual}"));
        throw new InvalidOperationException("WFP ABI layout mismatch vs Windows SDK: " + details);
    }
}