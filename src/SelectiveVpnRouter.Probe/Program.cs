using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SelectiveVpnRouter.Network;

if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
{
    Console.WriteLine("""
        SelectiveVpnRouter.Probe
          --show-network
          --smoke-network-catalog
          --tcp HOST PORT
          --http URL
          --dns HOST
          --watch
          --tcp6 HOST PORT
          --spawn EXE [args...]
          --via-proxy HOST:PORT
          --bind-if INDEX
          --public-ip [URL]

        Add this executable as an Application VPN rule, then --http to verify isolation.
        Without a rule, the same commands should stay DIRECT.
        --spawn starts another executable (policy follows that child, not this parent).
        """);
    return 0;
}

if (args.Contains("--smoke-network-catalog"))
{
    return RunNetworkCatalogSmoke();
}

if (args.Length >= 2 && args[0] == "--spawn")
{
    var psi = new ProcessStartInfo
    {
        FileName = args[1],
        UseShellExecute = false,
    };
    for (int i = 2; i < args.Length; i++)
    {
        psi.ArgumentList.Add(args[i]);
    }

    using var child = Process.Start(psi);
    if (child is null)
    {
        Console.WriteLine("spawn FAIL");
        return 1;
    }

    await child.WaitForExitAsync();
    return child.ExitCode;
}

int? bindIf = null;
string? proxy = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--bind-if" && i + 1 < args.Length && int.TryParse(args[i + 1], out int idx))
    {
        bindIf = idx;
    }

    if (args[i] == "--via-proxy" && i + 1 < args.Length)
    {
        proxy = args[i + 1];
    }
}

string self = Environment.ProcessPath ?? "SelectiveVpnRouter.Probe";
Console.WriteLine("process " + self);
Console.WriteLine("pid " + Environment.ProcessId);

if (args.Contains("--show-network"))
{
    PrintNetworkCatalog();
}

if (args.Contains("--watch"))
{
    Console.WriteLine("watch: Ctrl+C to stop");
    while (true)
    {
        Console.WriteLine(DateTimeOffset.Now.ToString("HH:mm:ss") + " defaults: " + RouteTable.DefaultRoutes().Count);
        await Task.Delay(2000);
    }
}

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--dns" && i + 1 < args.Length)
    {
        string host = args[i + 1];
        var sw = Stopwatch.StartNew();
        IPAddress[] addrs = await Dns.GetHostAddressesAsync(host);
        Console.WriteLine($"dns {host} {sw.ElapsedMilliseconds}ms {string.Join(",", addrs.Select(a => a.ToString()))}");
    }

    if (args[i] == "--tcp" && i + 2 < args.Length)
    {
        await TcpAsync(args[i + 1], int.Parse(args[i + 2]), bindIf, proxy);
    }

    if (args[i] == "--tcp6" && i + 2 < args.Length)
    {
        await Tcp6Async(args[i + 1], int.Parse(args[i + 2]));
    }

    if (args[i] == "--http" && i + 1 < args.Length)
    {
        await HttpAsync(args[i + 1], bindIf, proxy);
    }

    if (args[i] == "--public-ip")
    {
        string url = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[i + 1]
            : "https://api.ipify.org";
        await HttpAsync(url, bindIf, proxy);
    }
}

return 0;

static async Task Tcp6Async(string host, int port)
{
    var sw = Stopwatch.StartNew();
    try
    {
        IPAddress ip;
        if (!IPAddress.TryParse(host, out ip!) || ip.AddressFamily != AddressFamily.InterNetworkV6)
        {
            IPAddress[] addrs = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetworkV6);
            ip = addrs[0];
        }

        using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await socket.ConnectAsync(ip, port, cts.Token);
        var local = (IPEndPoint)socket.LocalEndPoint!;
        Console.WriteLine($"tcp6 OK {sw.ElapsedMilliseconds}ms local {local} remote {socket.RemoteEndPoint}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"tcp6 FAIL {sw.ElapsedMilliseconds}ms {ex.GetType().Name}: {ex.Message}");
    }
}

static async Task TcpAsync(string host, int port, int? bindIf, string? proxy)
{
    var sw = Stopwatch.StartNew();
    try
    {
        using Socket socket = await ConnectAsync(host, port, bindIf, proxy);
        var local = (IPEndPoint)socket.LocalEndPoint!;
        var remote = (IPEndPoint)socket.RemoteEndPoint!;
        Console.WriteLine($"tcp OK {sw.ElapsedMilliseconds}ms local {local} remote {remote} bind-if={bindIf} proxy={proxy ?? "none"}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"tcp FAIL {sw.ElapsedMilliseconds}ms {ex.GetType().Name}: {ex.Message}");
    }
}

static async Task HttpAsync(string url, int? bindIf, string? proxy)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
    {
        Console.WriteLine("http FAIL invalid URL");
        return;
    }

    var sw = Stopwatch.StartNew();
    try
    {
        using Socket socket = await ConnectAsync(uri.Host, uri.Port, bindIf, proxy);
        using var ns = new NetworkStream(socket, ownsSocket: true);
        Stream stream = ns;
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            var ssl = new System.Net.Security.SslStream(ns, false, static (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(uri.Host);
            stream = ssl;
        }

        using var writer = new StreamWriter(stream, leaveOpen: true) { NewLine = "\r\n" };
        await writer.WriteLineAsync("GET " + (string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery) + " HTTP/1.1");
        await writer.WriteLineAsync("Host: " + uri.Host);
        await writer.WriteLineAsync("Connection: close");
        await writer.WriteLineAsync("User-Agent: SelectiveVpnRouter.Probe/0.2");
        await writer.WriteLineAsync();
        await writer.FlushAsync();
        using var reader = new StreamReader(stream);
        string body = await reader.ReadToEndAsync();
        var local = (IPEndPoint)socket.LocalEndPoint!;
        Console.WriteLine($"http OK {sw.ElapsedMilliseconds}ms local {local} bytes={body.Length}");
        string snippet = body.Length > 300 ? body[..300] : body;
        Console.WriteLine(snippet.Trim());
    }
    catch (Exception ex)
    {
        Console.WriteLine($"http FAIL {sw.ElapsedMilliseconds}ms {ex.GetType().Name}: {ex.Message}");
    }
}

static async Task<Socket> ConnectAsync(string host, int port, int? bindIf, string? proxy)
{
    if (proxy is not null)
    {
        string[] parts = proxy.Split(':');
        var proxyEp = new IPEndPoint(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
        var socks = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socks.ConnectAsync(proxyEp);
        await SocksConnectAsync(socks, host, port);
        return socks;
    }

    IPAddress[] addrs = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork);
    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    if (bindIf is int idx)
    {
        SocketInterfaceBinder.BindIpv4UnicastIf(socket, idx);
    }

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await socket.ConnectAsync(addrs[0], port, cts.Token);
    return socket;
}

static async Task SocksConnectAsync(Socket socket, string host, int port)
{
    await socket.SendAsync(new byte[] { 5, 1, 0 });
    var hello = new byte[2];
    await RecvExact(socket, hello);
    if (hello[1] != 0)
    {
        throw new IOException("SOCKS5 method rejected");
    }

    byte[] hostBytes = System.Text.Encoding.ASCII.GetBytes(host);
    var req = new byte[5 + hostBytes.Length + 2];
    req[0] = 5;
    req[1] = 1;
    req[3] = 3;
    req[4] = (byte)hostBytes.Length;
    Buffer.BlockCopy(hostBytes, 0, req, 5, hostBytes.Length);
    req[^2] = (byte)(port >> 8);
    req[^1] = (byte)(port & 0xFF);
    await socket.SendAsync(req);
    var resp = new byte[10];
    int n = await socket.ReceiveAsync(resp);
    if (n < 2 || resp[1] != 0)
    {
        throw new IOException("SOCKS5 connect failed status=" + (n > 1 ? resp[1] : -1));
    }
}

static async Task RecvExact(Socket socket, byte[] buf)
{
    int got = 0;
    while (got < buf.Length)
    {
        int n = await socket.ReceiveAsync(buf.AsMemory(got));
        if (n == 0)
        {
            throw new IOException("EOF");
        }

        got += n;
    }
}

static int RunNetworkCatalogSmoke()
{
    Console.WriteLine("network-catalog-smoke: start");
    for (int pass = 1; pass <= 5; pass++)
    {
        IReadOnlyList<AdapterView> adapters = AdapterCatalog.All();
        IReadOnlyList<RouteRow> defaults = RouteTable.DefaultRoutes();
        Console.WriteLine($"pass={pass} adapters={adapters.Count} defaultRoutes={defaults.Count}");
    }

    PrintNetworkCatalog();
    Console.WriteLine("network-catalog-smoke: PASS");
    return 0;
}

static void PrintNetworkCatalog()
{
    foreach (AdapterView nic in AdapterCatalog.All())
    {
        string addrs = string.Join(
            ", ",
            nic.Ipv4TunnelAddresses.Select(a => a.Address + "/" + a.PrefixLength + " " + a.DadState));
        Console.WriteLine($"{nic.Name} [{nic.Status}] if4={nic.Ipv4Index} ipv4=[{addrs}] | {nic.Description}");
    }

    foreach (RouteRow r in RouteTable.DefaultRoutes())
    {
        Console.WriteLine($"default {r.Destination}/{r.Mask} via {r.NextHop} if {r.InterfaceIndex} metric {r.Metric}");
    }
}
