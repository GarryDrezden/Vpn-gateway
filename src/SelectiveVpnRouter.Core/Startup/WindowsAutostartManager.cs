namespace SelectiveVpnRouter.Core.Startup;

public sealed record WindowsAutostartPlan(
    bool ShouldWrite,
    bool ShouldDelete,
    string? CommandToWrite,
    bool IsCurrentRegistrationMatching);

public static class WindowsAutostartManager
{
    public const string ValueName = "VPN Route";

    public static string FormatRunCommand(string executablePath, bool includeAutostartMarker)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(executablePath));
        }

        string quoted = QuoteWindowsPath(executablePath);
        return includeAutostartMarker
            ? quoted + " --autostart"
            : quoted;
    }

    public static WindowsAutostartPlan PlanRegistration(
        bool startWithWindowsEnabled,
        string currentExecutablePath,
        string? registeredCommand,
        bool includeAutostartMarker = true)
    {
        string desired = FormatRunCommand(currentExecutablePath, includeAutostartMarker);
        if (!startWithWindowsEnabled)
        {
            bool hadOwn = registeredCommand is not null;
            return new WindowsAutostartPlan(false, hadOwn, null, !hadOwn);
        }

        bool matches = CommandsReferToSameExecutable(registeredCommand, desired, currentExecutablePath);
        return new WindowsAutostartPlan(!matches, false, desired, matches);
    }

    public static void ApplyPlan(IWindowsStartupRunKey runKey, WindowsAutostartPlan plan)
    {
        if (plan.ShouldDelete)
        {
            runKey.DeleteValue(ValueName);
        }

        if (plan.ShouldWrite && plan.CommandToWrite is not null)
        {
            runKey.WriteCommand(ValueName, plan.CommandToWrite);
        }
    }

    public static bool CommandsReferToSameExecutable(string? registeredCommand, string desiredCommand, string currentExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(registeredCommand))
        {
            return false;
        }

        string? registeredExe = TryExtractExecutablePath(registeredCommand);
        if (registeredExe is null)
        {
            return string.Equals(NormalizeCommand(registeredCommand), NormalizeCommand(desiredCommand), StringComparison.OrdinalIgnoreCase);
        }

        return PathsEqual(registeredExe, currentExecutablePath);
    }

    internal static string QuoteWindowsPath(string path)
    {
        string trimmed = path.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            return trimmed;
        }

        return "\"" + trimmed + "\"";
    }

    private static string NormalizeCommand(string command) => command.Trim();

    private static string? TryExtractExecutablePath(string command)
    {
        string trimmed = command.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.StartsWith('"'))
        {
            int end = trimmed.IndexOf('"', 1);
            if (end > 1)
            {
                return trimmed[1..end];
            }
        }

        int space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
