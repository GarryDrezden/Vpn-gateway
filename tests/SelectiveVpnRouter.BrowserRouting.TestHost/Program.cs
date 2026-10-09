using System.Globalization;
using SelectiveVpnRouter.Core.BrowserRouting;

// Test-only stand-in for the Service side of browser routing: the production store, dispatcher and
// pipe server, seeded with generated rules in a temporary directory. It never starts RouterEngine,
// never touches VPN, routes, WFP or %ProgramData%.
//
//   --pipe <name>        pipe name: the production name (default) or one in the
//                        "SelectiveVpnRouter.BrowserRouting.Test." namespace, so the test can run
//                        next to a live Service (the native host targets it via its test override)
//   --store <dir>        directory for browser-routing-state.json (required)
//   --rules <n>          number of generated rules (default 0)
//   --profile <p>        typical (default) | worst: every field at its maximum length, non-ASCII text
//   --default <route>    VPN | Direct (default Direct)
//   --proxy ready:<port> report a Ready loopback browser proxy (default Unavailable)
//
// stdout: "READY <generation> <revision> <ruleCount>" once listening, then one line per command.
// stdin:  bump | reset | churn on | churn off | quit

var options = ParseArgs(args);
var store = new BrowserRoutingStateStore(Path.Combine(options.Store, "browser-routing-state.json"));
if (store.Load() != BrowserRoutingStoreStatus.Available)
{
    Console.Error.WriteLine("store unavailable: " + store.UnavailableReason);
    return 2;
}
var rules = options.Worst ? GenerateWorst(options.Rules) : Generate(options.Rules);
if (rules.Count > 0 || options.DefaultRoute != BrowserRoutingContract.RouteDirect)
    store.Update(store.Current!.Revision, options.DefaultRoute, rules);

var churn = false;
var gate = new object();
void Bump()
{
    lock (gate)
        store.Update(store.Current!.Revision, options.DefaultRoute, rules);
}

IBrowserProxyReadiness readiness = options.ProxyPort is int port
    ? new FixedReadiness(new BrowserProxyStatus(BrowserProxyStatus.Ready, "127.0.0.1", port))
    : new UnavailableBrowserProxyReadiness();
var dispatcher = new BrowserRoutingIpcDispatcher(
    () => store.Current,
    store,
    readiness,
    new UnavailableTunnelEgress(),
    new BrowserClientTracker(),
    new TestServiceVersion(),
    new TestInterfaceLookup());
var server = new BrowserRoutingPipeServer(options.Pipe, dispatcher, line =>
{
    Console.Error.WriteLine(line);
    if (churn && line.StartsWith("browser-routing getPage: ok", StringComparison.Ordinal))
        Bump();
});

using var stop = new CancellationTokenSource();
var serving = Task.Run(() => server.RunAsync(stop.Token));
await Task.Delay(200);
if (serving.IsFaulted)
{
    Console.Error.WriteLine("pipe server failed: " + serving.Exception!.InnerException!.GetType().Name +
        " (is another process already serving " + options.Pipe + "?)");
    return 3;
}

Console.WriteLine($"READY {store.Current!.StateGeneration} {store.Current.Revision} {store.Current.RuleCount}");
string? command;
while ((command = Console.ReadLine()) is not null)
{
    switch (command.Trim())
    {
        case "bump":
            Bump();
            Console.WriteLine($"BUMPED {store.Current!.Revision}");
            break;
        case "reset":
            lock (gate)
            {
                store.Reset();
                store.Update(0, options.DefaultRoute, rules);
            }
            Console.WriteLine($"RESET {store.Current!.StateGeneration} {store.Current.Revision}");
            break;
        case "churn on":
            churn = true;
            Console.WriteLine("CHURN on");
            break;
        case "churn off":
            churn = false;
            Console.WriteLine("CHURN off");
            break;
        case "quit":
            await stop.CancelAsync();
            return 0;
    }
}
await stop.CancelAsync();
return 0;

static List<BrowserRoutingRule> Generate(int count)
{
    string[] modes = [BrowserRoutingContract.RouteVpn, BrowserRoutingContract.RouteDirect, BrowserRoutingContract.RouteDefault];
    var list = new List<BrowserRoutingRule>(count);
    for (var i = 0; i < count; i++)
    {
        var id = "e2e-" + i.ToString("D5", CultureInfo.InvariantCulture);
        var exact = i % 4 == 3;
        list.Add(new BrowserRoutingRule(
            id,
            "E2E rule " + i.ToString(CultureInfo.InvariantCulture) + " \u2014 \u043f\u0440\u0430\u0432\u0438\u043b\u043e",
            "site" + i.ToString(CultureInfo.InvariantCulture) + ".region" + (i % 50).ToString(CultureInfo.InvariantCulture) + ".e2e-routing.example",
            exact ? BrowserRoutingContract.ExactHost : BrowserRoutingContract.DomainAndSubdomains,
            modes[i % 3],
            i % 17 != 5,
            i % 29 == 0 ? BrowserRoutingContract.SourceSystem : BrowserRoutingContract.SourceUser,
            i % 7 == 0 ? "generated note " + i.ToString(CultureInfo.InvariantCulture) : null));
    }
    return list;
}

static List<BrowserRoutingRule> GenerateWorst(int count)
{
    var list = new List<BrowserRoutingRule>(count);
    for (var i = 0; i < count; i++)
    {
        var n = i.ToString("D5", CultureInfo.InvariantCulture);
        var first = ("w" + n).PadRight(BrowserRoutingContract.MaxLabelLength, 'x');
        var label = new string('a', BrowserRoutingContract.MaxLabelLength);
        list.Add(new BrowserRoutingRule(
            ("w-" + n).PadRight(BrowserRoutingContract.MaxIdLength, 'x'),
            n + new string('\u0416', BrowserRoutingContract.MaxNameLength - n.Length),
            first + "." + label + "." + label + "." + new string('b', BrowserRoutingContract.MaxHostLength - 3 * 64),
            i % 2 == 0 ? BrowserRoutingContract.DomainAndSubdomains : BrowserRoutingContract.ExactHost,
            i % 2 == 0 ? BrowserRoutingContract.RouteVpn : BrowserRoutingContract.RouteDirect,
            true,
            BrowserRoutingContract.SourceUser,
            new string('\u0416', BrowserRoutingContract.MaxNotesLength)));
    }
    return list;
}

static (string Pipe, string Store, int Rules, bool Worst, string DefaultRoute, int? ProxyPort) ParseArgs(string[] args)
{
    var pipe = BrowserRoutingIpcProtocol.PipeName;
    var worst = false;
    string? storeDir = null;
    var rules = 0;
    var defaultRoute = BrowserRoutingContract.RouteDirect;
    int? proxyPort = null;
    for (var i = 0; i + 1 < args.Length; i += 2)
    {
        switch (args[i])
        {
            case "--pipe" when args[i + 1].StartsWith(BrowserRoutingIpcProtocol.PipeName + ".Test.", StringComparison.Ordinal):
                pipe = args[i + 1];
                break;
            case "--store": storeDir = args[i + 1]; break;
            case "--profile" when args[i + 1] is "typical" or "worst":
                worst = args[i + 1] == "worst";
                break;
            case "--rules": rules = int.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
            case "--default": defaultRoute = args[i + 1]; break;
            case "--proxy" when args[i + 1].StartsWith("ready:", StringComparison.Ordinal):
                proxyPort = int.Parse(args[i + 1]["ready:".Length..], CultureInfo.InvariantCulture);
                break;
            default: throw new ArgumentException("Unknown argument " + args[i]);
        }
    }
    return (pipe, storeDir ?? throw new ArgumentException("--store is required"), rules, worst, defaultRoute, proxyPort);
}

internal sealed class FixedReadiness(BrowserProxyStatus status) : IBrowserProxyReadiness
{
    public BrowserProxyStatus GetStatus() => status;
}

internal sealed class UnavailableTunnelEgress : IVpnTunnelEgressReadiness
{
    public bool TryGetTunnelInterfaceIndex(out int interfaceIndex)
    {
        interfaceIndex = 0;
        return false;
    }
}

internal sealed class TestServiceVersion : IBrowserIntegrationServiceVersion
{
    public string ServiceVersion { get; } = "test-host";
}

internal sealed class TestInterfaceLookup : IVpnInterfaceNameLookup
{
    public string? TryGetInterfaceName(int interfaceIndex) => null;
}
