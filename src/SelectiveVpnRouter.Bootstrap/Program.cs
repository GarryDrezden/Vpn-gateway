using System.Text.Json;
using SelectiveVpnRouter.Core.Portable;

namespace SelectiveVpnRouter.Bootstrap;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return PortableBootstrapExitCodes.InvalidArguments;
            }

            string command = args[0].Trim().ToLowerInvariant();
            string? rootArg = PortableBootstrapCommandLine.ReadOption(args, "--root");
            string root = !string.IsNullOrWhiteSpace(rootArg)
                ? rootArg
                : Path.GetFullPath(AppContext.BaseDirectory);

            var engine = new PortableBootstrapEngine();
            switch (command)
            {
                case "status":
                    PortableBootstrapStatus status = engine.GetStatus(root);
                    Console.WriteLine(PortableBootstrapEngine.SerializeStatus(status));
                    return status.BootstrapState == PortableBootstrapState.Broken
                        ? PortableBootstrapExitCodes.Failed
                        : PortableBootstrapExitCodes.Success;
                case "repair":
                    PortableBootstrapCommandResult repair = engine.Repair(root);
                    PortableBootstrapResultIO.WriteLastResult("repair", repair);
                    if (repair.Status is not null)
                    {
                        Console.WriteLine(PortableBootstrapEngine.SerializeStatus(repair.Status));
                    }

                    if (!string.IsNullOrWhiteSpace(repair.Message))
                    {
                        Console.Error.WriteLine(repair.Message);
                    }

                    return repair.ExitCode;
                case "remove":
                    PortableBootstrapCommandResult remove = engine.Remove(root);
                    PortableBootstrapResultIO.WriteLastResult("remove", remove);
                    if (remove.Status is not null)
                    {
                        Console.WriteLine(PortableBootstrapEngine.SerializeStatus(remove.Status));
                    }

                    if (!string.IsNullOrWhiteSpace(remove.Message))
                    {
                        Console.Error.WriteLine(remove.Message);
                    }

                    return remove.ExitCode;
                case "help":
                case "--help":
                case "-h":
                    PrintHelp();
                    return PortableBootstrapExitCodes.Success;
                default:
                    Console.Error.WriteLine("Unknown command: " + command);
                    PrintHelp();
                    return PortableBootstrapExitCodes.InvalidArguments;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return PortableBootstrapExitCodes.Failed;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("SelectiveVpnRouter.Bootstrap.exe <status|repair|remove> [--root <portableDir>]");
    }
}