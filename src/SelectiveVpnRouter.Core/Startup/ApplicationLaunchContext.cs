namespace SelectiveVpnRouter.Core.Startup;

public sealed record ApplicationLaunchContext(bool LaunchedFromAutostartMarker)
{
    public static ApplicationLaunchContext Parse(IEnumerable<string> args)
    {
        bool marker = args.Any(a => string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase));
        return new ApplicationLaunchContext(marker);
    }
}
