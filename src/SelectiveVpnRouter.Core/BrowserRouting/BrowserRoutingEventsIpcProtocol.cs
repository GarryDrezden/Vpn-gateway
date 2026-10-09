namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>
/// Browser routing push events IPC v1. Same framing as <see cref="BrowserRoutingIpcProtocol"/>,
/// but connections are long-lived: one subscribe request, one ack, then server-pushed event frames.
/// </summary>
public static class BrowserRoutingEventsIpcProtocol
{
    public const string PipeName = "SelectiveVpnRouter.BrowserRouting.Events";
    public const int Version = 1;

    public const int MaxRequestBytes = 1024;
    public const int MaxEventBytes = 1024;

    public static class Methods
    {
        public const string SubscribeEvents = "subscribeEvents";
    }

    public static class EventTypes
    {
        public const string BrowserRoutingChanged = BrowserRoutingChangeNotifier.BrowserRoutingChanged;
        public const string ServiceAvailable = BrowserRoutingChangeNotifier.ServiceAvailable;
    }
}
