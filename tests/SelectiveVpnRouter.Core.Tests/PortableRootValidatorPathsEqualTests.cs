using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class PortableRootValidatorPathsEqualTests
{
    private const string DriverSys =
        @"C:\Temp\VPN Route Portable UAC Test\driver\SelectiveVpnCallout.sys";

    [Fact]
    public void A_same_driver_sys_path_matches()
    {
        Assert.True(PortableRootValidator.PathsEqual(DriverSys, DriverSys));
    }

    [Fact]
    public void B_case_insensitive_match()
    {
        Assert.True(PortableRootValidator.PathsEqual(
            DriverSys,
            DriverSys.ToUpperInvariant()));
    }

    [Fact]
    public void C_spaced_path_segments_match_trailing_separator_variant()
    {
        Assert.True(PortableRootValidator.PathsEqual(DriverSys + "\\", DriverSys));
    }

    [Fact]
    public void D_different_sys_filename_false()
    {
        Assert.False(PortableRootValidator.PathsEqual(
            @"C:\Temp\VPN Route Portable UAC Test\driver\Other.sys",
            DriverSys));
    }

    [Fact]
    public void E_different_root_false()
    {
        Assert.False(PortableRootValidator.PathsEqual(
            @"C:\Temp\Other Portable UAC Test\driver\SelectiveVpnCallout.sys",
            DriverSys));
    }

    [Fact]
    public void F_sys_path_not_truncated_at_whitespace()
    {
        string expected = PortableLayout.ExpectedDriverSysPath(@"C:\Temp\VPN Route Portable UAC Test");
        Assert.True(PortableRootValidator.PathsEqual(expected, DriverSys));
        Assert.False(PortableRootValidator.PathsEqual(@"C:\Temp\VPN", DriverSys));
    }

    [Fact]
    public void G_exe_filesystem_path_with_spaces_matches()
    {
        string exe = @"C:\Temp\VPN Route Portable UAC Test\SelectiveVpnRouter.Service.exe";
        Assert.True(PortableRootValidator.PathsEqual(exe, exe));
    }
}