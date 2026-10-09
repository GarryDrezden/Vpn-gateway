using SelectiveVpnRouter.Core.BrowserRouting;

namespace SelectiveVpnRouter.Service;

public sealed class EntryAssemblyBrowserIntegrationServiceVersion : IBrowserIntegrationServiceVersion
{
    public string ServiceVersion { get; } =
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
}
