namespace SelectiveVpnRouter.Core;

public static class ProfileSafety
{
    private static readonly HashSet<string> Dangerous = new(StringComparer.OrdinalIgnoreCase)
    {
        "redirect-gateway",
        "redirect-private",
        "route",
        "route-ipv6",
        "dhcp-option",
        "dns",
        "block-outside-dns",
    };

    private static readonly HashSet<string> DataTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "ca", "cert", "key", "tls-auth", "tls-crypt", "tls-crypt-v2",
        "secret", "extra-certs", "pkcs12", "dh", "crl-verify",
    };

    public sealed record Finding(int Line, string Directive);

    public sealed record Result(bool Ok, IReadOnlyList<Finding> Findings, bool AuthInteraction, string? Error);

    public static Result Scan(IEnumerable<string> lines)
    {
        var findings = new List<Finding>();
        bool inData = false, inPem = false, auth = false;
        int n = 0;
        foreach (string raw in lines)
        {
            n++;
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
                    inData = false;
                }
                else if (DataTags.Contains(tag))
                {
                    inData = true;
                }

                continue;
            }

            if (inData || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                continue;
            }

            string directive = FirstToken(trimmed);
            if (Dangerous.Contains(directive))
            {
                findings.Add(new Finding(n, directive));
            }

            if (directive.Equals("auth-user-pass", StringComparison.OrdinalIgnoreCase) && TokenCount(trimmed) < 2)
            {
                auth = true;
            }
        }

        return new Result(findings.Count == 0, findings, auth, inPem || inData ? "Unclosed inline block." : null);
    }

    private static string FirstToken(string line)
    {
        string[] parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "" : parts[0];
    }

    private static int TokenCount(string line)
        => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
