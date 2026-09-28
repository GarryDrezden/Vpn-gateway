using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Network;

public interface IWfpAppFilterInstaller
{
    bool DriverPresent { get; }

    WfpPolicyApplyResult ReplaceVpnAppFilters(IReadOnlyList<string> exePaths, Ipv6Policy ipv6);

    void ClearFilters();
}