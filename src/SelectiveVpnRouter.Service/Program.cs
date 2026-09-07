using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SelectiveVpnRouter.Service;

string mode = args.FirstOrDefault() ?? "";
if (mode is "--help" or "-h")
{
    Console.WriteLine("SelectiveVpnRouter.Service [--console]");
    Console.WriteLine("Elevated backend: OpenVPN, owned routes, WFP, local TCP proxy, named pipe IPC.");
    return 0;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "SelectiveVpnRouter");
builder.Services.AddSingleton<RouterEngine>();
builder.Services.AddHostedService<PipeIpcHost>();
if (!mode.Equals("--console", StringComparison.OrdinalIgnoreCase)
    && !Environment.UserInteractive)
{
    // Windows Service default
}

using IHost host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
return 0;
