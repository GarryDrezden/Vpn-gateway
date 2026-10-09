using SelectiveVpnRouter.Core.BrowserRouting;

namespace SelectiveVpnRouter.Service;

public sealed partial class RouterEngine
{
    private readonly Lazy<IBrowserIntegrationSnapshotProvider> _browserIntegrationSnapshot;
}
