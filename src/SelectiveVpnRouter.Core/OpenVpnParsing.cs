namespace SelectiveVpnRouter.Core;

public static class OpenVpnStateParser
{
    public static bool IsConnected(string logLine)
        => logLine.Contains("Initialization Sequence Completed", StringComparison.OrdinalIgnoreCase);

    public static bool IsAuthPrompt(string logLine)
        => logLine.Contains("Enter Auth Username", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("Enter Auth Password", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("Enter Private Key Password", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("AUTH_FAILED", StringComparison.OrdinalIgnoreCase);

    public static bool IsFatal(string logLine)
        => logLine.Contains("FATAL", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("Options error", StringComparison.OrdinalIgnoreCase);

    public static string? TryParseVersionLine(string combinedOutput)
        => combinedOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static l => l.Trim())
            .FirstOrDefault(static l => l.StartsWith("OpenVPN ", StringComparison.Ordinal));

    public static string? TryParseRouteGateway(string logLine)
    {
        const string key = "route-gateway ";
        int i = logLine.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return null;
        }

        string rest = logLine[(i + key.Length)..];
        string token = rest.Split(',', ' ', '\t')[0].Trim();
        return System.Net.IPAddress.TryParse(token, out _) ? token : null;
    }

    public static (string? Local, string? PeerOrMask)? TryParseIfconfig(string logLine)
    {
        const string key = "ifconfig ";
        int i = logLine.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return null;
        }

        string[] parts = logLine[(i + key.Length)..].Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        if (!System.Net.IPAddress.TryParse(parts[0], out _) || !System.Net.IPAddress.TryParse(parts[1], out _))
        {
            return null;
        }

        return (parts[0], parts[1]);
    }

    public static bool LooksLikeMissingVpnGateway(IEnumerable<string> lines)
        => lines.Any(l =>
            l.Contains("vpn_gateway", StringComparison.OrdinalIgnoreCase)
            && (l.Contains("error", StringComparison.OrdinalIgnoreCase)
                || l.Contains("fail", StringComparison.OrdinalIgnoreCase)
                || l.Contains("undef", StringComparison.OrdinalIgnoreCase)
                || l.Contains("cannot", StringComparison.OrdinalIgnoreCase)));
}

public static class LogRedactor
{
    private static readonly string[] Tokens =
    [
        "password", "passwd", "passphrase", "auth-token", "auth_token",
        "private key", "pkcs12", "credentials", "-----begin",
    ];

    public static string Redact(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        foreach (string token in Tokens)
        {
            if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return "[REDACTED]";
            }
        }

        return line;
    }
}
