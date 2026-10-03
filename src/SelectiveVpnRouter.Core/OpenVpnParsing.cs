namespace SelectiveVpnRouter.Core;

public static class OpenVpnStateParser
{
    public const string InitializationSequenceCompleted = "Initialization Sequence Completed";
    public const string InitializationSequenceCompletedWithErrors = "Initialization Sequence Completed With Errors";

    /// <summary>
    /// True only for a successful OpenVPN init completion line (not "With Errors").
    /// Accepts optional timestamp/text prefix; rejects partial or trailing extra tokens.
    /// </summary>
    public static bool IsConnected(string? logLine)
    {
        if (string.IsNullOrWhiteSpace(logLine))
        {
            return false;
        }

        string trimmed = logLine.Trim();
        if (trimmed.Contains(InitializationSequenceCompletedWithErrors, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!trimmed.EndsWith(InitializationSequenceCompleted, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public static bool IsAuthPrompt(string logLine)
        => logLine.Contains("Enter Auth Username", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("Enter Auth Password", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("Enter Private Key Password", StringComparison.OrdinalIgnoreCase);

    public static bool IsAuthFailed(string logLine)
        => logLine.Contains("AUTH_FAILED", StringComparison.OrdinalIgnoreCase);

    /// <summary>Auth-related OpenVPN stderr/stdout worth persisting (sanitized before log).</summary>
    public static bool IsWorkVpnAuthRelatedLogLine(string? logLine)
    {
        if (string.IsNullOrWhiteSpace(logLine))
        {
            return false;
        }

        return logLine.Contains("AUTH", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("AUTH_FAILED", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("AUTH_PENDING", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("CRV1", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("CHALLENGE", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("AWAIT_AUTH", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("SIGTERM[soft,auth-failure]", StringComparison.OrdinalIgnoreCase)
            || IsAuthPrompt(logLine);
    }

    public static bool IsAuthPending(string logLine)
        => logLine.Contains("AUTH_PENDING", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("Pending Auth", StringComparison.OrdinalIgnoreCase);

    public static bool SuggestsWaitingForExternalMfa(string logLine)
        => IsAuthPending(logLine)
            || logLine.Contains("AWAIT_AUTH", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("authentication pending", StringComparison.OrdinalIgnoreCase);

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

public enum WorkVpnAuthResponseKind
{
    None = 0,
    WrongCredentials = 1,
    ChallengeRequired = 2,
    MfaPending = 3,
    AuthRejected = 4,
    UnknownAuthResponse = 5,
}

/// <summary>
/// Classifies OpenVPN auth control/log lines. Do not treat every AUTH_FAILED as terminal wrong password.
/// </summary>
public static class WorkVpnAuthLineClassifier
{
    public static WorkVpnAuthResponseKind Classify(string? logLine)
    {
        if (string.IsNullOrWhiteSpace(logLine))
        {
            return WorkVpnAuthResponseKind.None;
        }

        if (OpenVpnStateParser.SuggestsWaitingForExternalMfa(logLine))
        {
            return WorkVpnAuthResponseKind.MfaPending;
        }

        if (logLine.Contains("CHALLENGE:", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("CRV1", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("static challenge", StringComparison.OrdinalIgnoreCase))
        {
            return WorkVpnAuthResponseKind.ChallengeRequired;
        }

        if (!OpenVpnStateParser.IsAuthFailed(logLine))
        {
            return WorkVpnAuthResponseKind.None;
        }

        int failedIdx = logLine.IndexOf("AUTH_FAILED", StringComparison.OrdinalIgnoreCase);
        string tail = logLine[(failedIdx + "AUTH_FAILED".Length)..].TrimStart();
        if (tail.StartsWith(",", StringComparison.Ordinal))
        {
            string payload = tail[1..].Trim();
            if (payload.Contains("CRV1", StringComparison.OrdinalIgnoreCase)
                || payload.Contains("CHALLENGE", StringComparison.OrdinalIgnoreCase))
            {
                return WorkVpnAuthResponseKind.ChallengeRequired;
            }

            if (payload.Length > 0)
            {
                return WorkVpnAuthResponseKind.UnknownAuthResponse;
            }
        }

        return WorkVpnAuthResponseKind.WrongCredentials;
    }

    public static string UserFacingMessage(WorkVpnAuthResponseKind kind) => kind switch
    {
        WorkVpnAuthResponseKind.WrongCredentials => "Authentication failed.",
        WorkVpnAuthResponseKind.AuthRejected => "Authentication rejected by the server.",
        WorkVpnAuthResponseKind.ChallengeRequired => "Additional authentication is required.",
        WorkVpnAuthResponseKind.MfaPending => "Waiting for MFA approval.",
        WorkVpnAuthResponseKind.UnknownAuthResponse => "Unexpected authentication response from the server.",
        _ => "",
    };
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
