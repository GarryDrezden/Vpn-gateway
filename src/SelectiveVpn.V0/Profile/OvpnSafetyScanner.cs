namespace SelectiveVpn.V0.Profile;

/// <summary>
/// Conservative local-profile scanner. --route-nopull blocks pushed server routes/DNS,
/// but it does not neutralize directives that are already written in the .ovpn itself.
/// </summary>
internal static class OvpnSafetyScanner
{
    private static readonly HashSet<string> RoutingOrDnsDirectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "redirect-gateway",
        "redirect-private",
        "route",
        "route-ipv6",
        "dhcp-option",
        "dns",
        "block-outside-dns",
    };

    private static readonly HashSet<string> InlineDataTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "ca",
        "cert",
        "key",
        "tls-auth",
        "tls-crypt",
        "tls-crypt-v2",
        "secret",
        "extra-certs",
        "pkcs12",
        "dh",
        "crl-verify",
    };

    internal sealed record Finding(int LineNumber, string Directive);

    internal sealed record ScanResult(
        bool Ok,
        bool AuthInteractionRequired,
        string? AuthReason,
        IReadOnlyList<Finding> Findings,
        string? ParseError);

    public static ScanResult Scan(string profilePath)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(profilePath);
        }
        catch (Exception ex)
        {
            return new ScanResult(false, false, null, [], $"Cannot read profile: {ex.Message}");
        }

        var findings = new List<Finding>();
        bool inDataBlock = false;
        bool authInteraction = false;
        string? authReason = null;
        bool inPem = false;

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNumber = i + 1;
            string raw = lines[i];
            string trimmed = raw.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            if (inPem)
            {
                if (trimmed.StartsWith("-----END", StringComparison.OrdinalIgnoreCase))
                {
                    inPem = false;
                }

                continue;
            }

            if (trimmed.StartsWith("-----BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                inPem = true;
                continue;
            }

            if (trimmed.StartsWith('<') && trimmed.EndsWith('>') && trimmed.Length > 2)
            {
                string tag = trimmed[1..^1].Trim();
                if (tag.StartsWith('/'))
                {
                    inDataBlock = false;
                    continue;
                }

                if (InlineDataTags.Contains(tag))
                {
                    inDataBlock = true;
                    continue;
                }

                // <connection> and similar stay parsed so a nested redirect-gateway is not missed.
                continue;
            }

            if (inDataBlock)
            {
                continue;
            }

            if (trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                continue;
            }

            string directive = FirstToken(trimmed);
            if (directive.Length == 0)
            {
                continue;
            }

            if (RoutingOrDnsDirectives.Contains(directive))
            {
                findings.Add(new Finding(lineNumber, directive));
                continue;
            }

            if (directive.Equals("auth-user-pass", StringComparison.OrdinalIgnoreCase)
                && CountTokens(trimmed) < 2
                && !authInteraction)
            {
                authInteraction = true;
                authReason = "Profile uses auth-user-pass without a credentials file.";
            }

            if (directive.Equals("askpass", StringComparison.OrdinalIgnoreCase)
                && CountTokens(trimmed) < 2
                && !authInteraction)
            {
                authInteraction = true;
                authReason = "Profile uses askpass without a passphrase file.";
            }
        }

        if (inPem || inDataBlock)
        {
            return new ScanResult(
                false,
                authInteraction,
                authReason,
                findings,
                "Profile parser is ambiguous: an inline PEM/data block was not closed. V0 stops rather than guess.");
        }

        bool ok = findings.Count == 0;
        return new ScanResult(ok, authInteraction, authReason, findings, null);
    }

    private static string FirstToken(string line)
    {
        int i = 0;
        while (i < line.Length && char.IsWhiteSpace(line[i]))
        {
            i++;
        }

        int start = i;
        while (i < line.Length && !char.IsWhiteSpace(line[i]))
        {
            i++;
        }

        return start == i ? string.Empty : line[start..i];
    }

    private static int CountTokens(string line)
    {
        int count = 0;
        bool inToken = false;
        foreach (char c in line)
        {
            if (char.IsWhiteSpace(c))
            {
                inToken = false;
            }
            else if (!inToken)
            {
                inToken = true;
                count++;
            }
        }

        return count;
    }
}
