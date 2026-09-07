namespace SelectiveVpnRouter.Core;

public static class ExecutablePathNormalizer
{
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string trimmed = path.Trim().Trim('"');
        bool hasDirectory = trimmed.Contains('\\') || trimmed.Contains('/')
            || trimmed.Contains(':');
        if (hasDirectory)
        {
            try
            {
                trimmed = Path.GetFullPath(trimmed);
            }
            catch (Exception)
            {
            }
        }

        return trimmed.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
    }

    public static string FileName(string? path)
    {
        string n = Normalize(path);
        return string.IsNullOrEmpty(n) ? string.Empty : Path.GetFileName(n);
    }

    public static bool EqualsNormalized(string? a, string? b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
}
