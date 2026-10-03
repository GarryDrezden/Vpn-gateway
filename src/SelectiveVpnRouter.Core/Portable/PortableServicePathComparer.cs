namespace SelectiveVpnRouter.Core.Portable;

public static class PortableServicePathComparer
{
    public static bool ServicePathsMatch(string? registeredImagePath, string? expectedServiceExePath)
        => TryCompare(registeredImagePath, expectedServiceExePath, out _, out _, out _);

    public static bool TryCompare(
        string? registeredImagePath,
        string? expectedServiceExePath,
        out string? normalizedRegisteredExe,
        out string? normalizedExpectedExe,
        out string? mismatchReason)
    {
        normalizedRegisteredExe = null;
        normalizedExpectedExe = null;
        mismatchReason = null;

        if (!TryExtractExecutablePath(registeredImagePath, out normalizedRegisteredExe, out string? regError))
        {
            mismatchReason = regError ?? "Registered ImagePath is missing or invalid.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(expectedServiceExePath))
        {
            mismatchReason = "Expected service executable path is empty.";
            return false;
        }

        try
        {
            normalizedExpectedExe = Path.GetFullPath(expectedServiceExePath.Trim().Trim('"'));
        }
        catch (Exception ex)
        {
            mismatchReason = "Expected service path is invalid: " + ex.Message;
            return false;
        }

        bool match = string.Equals(
            normalizedRegisteredExe.TrimEnd('\\'),
            normalizedExpectedExe.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

        if (!match)
        {
            mismatchReason = "Registered service executable does not match portable package service path.";
        }

        return match;
    }

    public static bool TryExtractExecutablePath(string? imagePath, out string normalizedExecutable, out string? error)
    {
        normalizedExecutable = "";
        error = null;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            error = "ImagePath is empty.";
            return false;
        }

        string trimmed = imagePath.Trim();
        string executablePortion;
        if (trimmed.StartsWith('"'))
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote < 0)
            {
                error = "ImagePath has an unclosed quote.";
                return false;
            }

            executablePortion = trimmed[1..endQuote];
        }
        else
        {
            int exeSuffix = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeSuffix >= 0)
            {
                executablePortion = trimmed[..(exeSuffix + 4)];
            }
            else
            {
                int space = IndexOfExecutableArgumentSeparator(trimmed);
                executablePortion = space < 0 ? trimmed : trimmed[..space];
            }
        }

        executablePortion = Environment.ExpandEnvironmentVariables(executablePortion.Trim());
        if (string.IsNullOrWhiteSpace(executablePortion))
        {
            error = "ImagePath does not contain an executable.";
            return false;
        }

        try
        {
            normalizedExecutable = Path.GetFullPath(executablePortion);
            return true;
        }
        catch (Exception ex)
        {
            error = "ImagePath executable is invalid: " + ex.Message;
            return false;
        }
    }

    private static int IndexOfExecutableArgumentSeparator(string text)
    {
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                return i;
            }
        }

        return -1;
    }
}