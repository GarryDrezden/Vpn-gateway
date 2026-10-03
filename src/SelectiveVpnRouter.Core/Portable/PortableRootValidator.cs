namespace SelectiveVpnRouter.Core.Portable;

public static class PortableRootValidator
{
    public static bool TryNormalizePortableRoot(string? candidate, out string normalized, out string? error)
    {
        normalized = "";
        error = null;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            error = "Portable root is empty.";
            return false;
        }

        string trimmed = candidate.Trim();
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            error = "UNC portable root is not supported in V1.";
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(trimmed);
        }
        catch (Exception ex)
        {
            error = "Invalid portable root: " + ex.Message;
            return false;
        }

        if (!Path.IsPathRooted(normalized))
        {
            error = "Portable root must be absolute.";
            return false;
        }

        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            error = "Portable root must not contain traversal segments.";
            return false;
        }

        if (!Directory.Exists(normalized))
        {
            error = "Portable root directory does not exist.";
            return false;
        }

        return true;
    }

    public static bool IsPathInsidePortableRoot(string portableRoot, string candidatePath)
    {
        if (!TryNormalizePortableRoot(portableRoot, out string root, out _))
        {
            return false;
        }

        try
        {
            string full = Path.GetFullPath(candidatePath);
            string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool PathsEqual(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        try
        {
            string left = NormalizeFilesystemPath(a);
            string right = NormalizeFilesystemPath(b);
            return string.Equals(
                left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string NormalizeFilesystemPath(string path)
    {
        string trimmed = path.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1];
        }

        trimmed = Environment.ExpandEnvironmentVariables(trimmed);
        return Path.GetFullPath(trimmed);
    }
}
