namespace SelectiveVpnRouter.Core.Portable;

public static class PortableBootstrapCommandLine
{
    public static string FormatRootForArgument(string normalizedPortableRoot)
    {
        return normalizedPortableRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string BuildRepairArguments(string normalizedPortableRoot)
        => "repair --root \"" + FormatRootForArgument(normalizedPortableRoot) + "\"";

    public static string BuildRemoveArguments(string normalizedPortableRoot)
        => "remove --root \"" + FormatRootForArgument(normalizedPortableRoot) + "\"";

    public static string? ReadOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}