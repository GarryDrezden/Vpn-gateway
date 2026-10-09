using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.BrowserRouting;

namespace SelectiveVpnRouter.App;

internal sealed class BrowserRoutingTransport(ServiceClient client) : IBrowserRoutingControlTransport
{
    public Task<IpcResponse> InvokeAsync(string method, object? payload, CancellationToken cancellationToken) =>
        client.SendAsync(method, payload, cancellationToken);
}
