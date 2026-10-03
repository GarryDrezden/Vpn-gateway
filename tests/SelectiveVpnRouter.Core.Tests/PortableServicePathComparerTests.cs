using SelectiveVpnRouter.Core.Portable;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class PortableServicePathComparerTests
{
    private const string PortableExe = @"C:\Temp\VPN Route Portable Test\SelectiveVpnRouter.Service.exe";

    [Fact]
    public void A_same_unquoted_portable_path_matches()
    {
        Assert.True(PortableServicePathComparer.ServicePathsMatch(PortableExe, PortableExe));
    }

    [Fact]
    public void B_quoted_registered_path_matches()
    {
        Assert.True(PortableServicePathComparer.ServicePathsMatch("\"" + PortableExe + "\"", PortableExe));
    }

    [Fact]
    public void C_case_difference_matches()
    {
        Assert.True(PortableServicePathComparer.ServicePathsMatch(
            PortableExe.ToUpperInvariant(),
            PortableExe.ToLowerInvariant()));
    }

    [Fact]
    public void D_trailing_separator_on_expected_matches()
    {
        Assert.True(PortableServicePathComparer.ServicePathsMatch(
            PortableExe,
            PortableExe + "\\"));
    }

    [Fact]
    public void E_different_portable_folder_false()
    {
        Assert.False(PortableServicePathComparer.ServicePathsMatch(
            @"C:\Temp\VPN Route Portable Relocated\SelectiveVpnRouter.Service.exe",
            PortableExe));
    }

    [Fact]
    public void F_old_repo_path_false()
    {
        Assert.False(PortableServicePathComparer.ServicePathsMatch(
            @"E:\Работа\OSPanel\domains\vpn-gateway\artifacts\publish\SelectiveVpnRouter.Service.exe",
            PortableExe));
    }

    [Fact]
    public void G_unquoted_path_with_spaces_matches()
    {
        Assert.True(PortableServicePathComparer.ServicePathsMatch(PortableExe, PortableExe));
    }

    [Fact]
    public void H_malformed_image_path_returns_false_without_throw()
    {
        Assert.False(PortableServicePathComparer.TryExtractExecutablePath("\"C:\\open.exe", out _, out string? err));
        Assert.False(string.IsNullOrWhiteSpace(err));
    }


    [Fact]
    public void J_service_exe_with_args_matches_expected_exe_only()
    {
        Assert.True(PortableServicePathComparer.ServicePathsMatch(
            "\"" + PortableExe + "\" --service",
            PortableExe));
    }
    [Fact]
    public void Sc_parser_russian_binary_path_line()
    {
        string line = "        \u0418\u043C\u044F_\u0434\u0432\u043E\u0438\u0447\u043D\u043E\u0433\u043E_\u0444\u0430\u0439\u043B\u0430   : " + PortableExe;
        Assert.True(WindowsSystemBootstrapProbe.TryParseScQcPathLine(line, out string? path));
        Assert.Equal(PortableExe, path);
    }
}