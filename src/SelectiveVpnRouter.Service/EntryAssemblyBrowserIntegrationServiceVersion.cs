using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.BrowserRouting;

namespace SelectiveVpnRouter.Service;

public sealed class EntryAssemblyBrowserIntegrationServiceVersion : IBrowserIntegrationServiceVersion
{
    public string ServiceVersion { get; } = ProductVersionInfo.FileVersion;
}
