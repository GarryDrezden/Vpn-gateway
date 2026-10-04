using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Service;

TextEncodingBootstrap.EnsureRegistered();

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    ServiceFatalLogger.Write("AppDomain unhandled exception", e.ExceptionObject as Exception);
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    ServiceFatalLogger.Write("TaskScheduler unobserved exception", e.Exception);
    e.SetObserved();
};

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
builder.Services.AddHostedService<BrowserRoutingPipeHost>();
if (!mode.Equals("--console", StringComparison.OrdinalIgnoreCase)
    && !Environment.UserInteractive)
{
    // Windows Service default
}

using IHost host = builder.Build();
IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    ServiceRuntimeContext.MarkHostStopping();
    ServiceFatalLogger.Write("Host ApplicationStopping");
});

try
{
    await host.RunAsync().ConfigureAwait(false);
}
catch (Exception ex)
{
    ServiceFatalLogger.Write("Host RunAsync terminated", ex);
    throw;
}
return 0;
