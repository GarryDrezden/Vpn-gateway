namespace SelectiveVpn.V0.OpenVpn;

/// <summary>
/// Strips likely secrets from OpenVPN stdout/stderr before they reach the console.
/// Verb 3 can mention auth material; V0 must never print it.
/// </summary>
internal static class OpenVpnLogRedactor
{
    private static readonly string[] SensitiveTokens =
    [
        "password",
        "passwd",
        "passphrase",
        "auth-token",
        "auth_token",
        "authtoken",
        "private key",
        "pkcs12",
        "credentials",
        "-----begin",
    ];

    public static string Redact(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        foreach (string token in SensitiveTokens)
        {
            if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return "[REDACTED sensitive OpenVPN log line]";
            }
        }

        return line;
    }
}
