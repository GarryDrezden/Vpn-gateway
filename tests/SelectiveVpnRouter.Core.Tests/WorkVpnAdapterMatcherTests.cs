using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WorkVpnAdapterMatcherTests
{
    [Fact]
    public void Selects_changed_work_adapter_not_unchanged_external()
    {
        var before = new List<WorkVpnAdapterSnapshot>
        {
            Snap("external", 24, "10.50.1.10", newborn: false, changed: false),
            Snap("selective", 8, "10.8.0.2", newborn: false, changed: false),
        };
        var after = new List<WorkVpnAdapterSnapshot>
        {
            Snap("external", 24, "10.50.1.10", newborn: false, changed: false),
            Snap("selective", 8, "10.8.0.2", newborn: false, changed: false),
            Snap("work", 55, "10.120.40.70", newborn: true, changed: true),
        };

        string? id = WorkVpnAdapterMatcher.TrySelectAdapterId(
            before,
            after,
            "10.120.40.70",
            excludeAdapterId: "selective",
            excludeInterfaceIndex: 8);
        Assert.Equal("work", id);
    }

    [Fact]
    public void Does_not_select_external_adapter_without_change()
    {
        var before = new List<WorkVpnAdapterSnapshot>
        {
            Snap("external", 24, "10.120.40.70", newborn: false, changed: false),
        };
        var after = new List<WorkVpnAdapterSnapshot>
        {
            Snap("external", 24, "10.120.40.70", newborn: false, changed: false),
        };

        string? id = WorkVpnAdapterMatcher.TrySelectAdapterId(
            before,
            after,
            "10.120.40.70",
            excludeAdapterId: null,
            excludeInterfaceIndex: null);
        Assert.Null(id);
    }

    [Fact]
    public void Excludes_selective_adapter_even_with_matching_ip()
    {
        var before = new List<WorkVpnAdapterSnapshot>();
        var after = new List<WorkVpnAdapterSnapshot>
        {
            Snap("selective", 8, "10.120.40.70", newborn: true, changed: true),
            Snap("work", 55, "10.120.40.71", newborn: true, changed: true),
        };

        string? id = WorkVpnAdapterMatcher.TrySelectAdapterId(
            before,
            after,
            "10.120.40.71",
            excludeAdapterId: "selective",
            excludeInterfaceIndex: 8);
        Assert.Equal("work", id);
    }

    private static WorkVpnAdapterSnapshot Snap(
        string id,
        int ifIndex,
        string ip,
        bool newborn,
        bool changed)
        => new(id, ifIndex, [ip], newborn, changed);
}