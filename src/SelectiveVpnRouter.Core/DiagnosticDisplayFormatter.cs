namespace SelectiveVpnRouter.Core;

public static class DiagnosticDisplayFormatter
{
    public static string FormatForUi(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        string trimmed = line.Trim();
        int spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx <= 0)
        {
            return trimmed;
        }

        string outcome = trimmed[..spaceIdx];
        string rest = trimmed[(spaceIdx + 1)..].TrimStart();
        int colon = rest.IndexOf(':');
        if (colon <= 0)
        {
            return trimmed;
        }

        string name = rest[..colon].Trim();
        string message = rest[(colon + 1)..].TrimStart();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(outcome + " " + name);
        sb.AppendLine();

        if (message.StartsWith("CONTROL FAIL", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("VPN-routed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("redirectFilter=[", StringComparison.Ordinal))
        {
            AppendStructuredLoopbackMessage(sb, message);
        }
        else
        {
            sb.Append(WrapLongLine(message, 96));
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendStructuredLoopbackMessage(System.Text.StringBuilder sb, string message)
    {
        int summaryEnd = message.IndexOf("probePath=", StringComparison.Ordinal);
        if (summaryEnd > 0)
        {
            sb.AppendLine(WrapLongLine(message[..summaryEnd].Trim(), 96));
            sb.AppendLine();
            message = message[summaryEnd..];
        }

        foreach (string segment in SplitDiagnosticSegments(message))
        {
            if (segment.StartsWith("probePath=", StringComparison.Ordinal))
            {
                sb.AppendLine("Probe:");
                AppendKeyValueLines(sb, "  ", segment);
            }
            else if (segment.StartsWith("redirectFilter=[", StringComparison.Ordinal))
            {
                sb.AppendLine("Redirect filter:");
                AppendFilterSegment(sb, segment, "redirectFilter=");
            }
            else if (segment.StartsWith("loopbackPermitFilter=[", StringComparison.Ordinal))
            {
                sb.AppendLine("Loopback permit:");
                AppendFilterSegment(sb, segment, "loopbackPermitFilter=");
            }
            else if (segment.StartsWith("probe:", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("Probe output:");
                sb.AppendLine(WrapLongLine(segment, 96));
            }
            else if (!string.IsNullOrWhiteSpace(segment))
            {
                sb.AppendLine(WrapLongLine(segment, 96));
            }

            sb.AppendLine();
        }
    }

    private static IEnumerable<string> SplitDiagnosticSegments(string message)
    {
        string[] markers =
        [
            " redirectFilter=",
            " loopbackPermitFilter=",
            " probe:",
        ];

        int start = 0;
        while (start < message.Length)
        {
            int next = message.Length;
            foreach (string marker in markers)
            {
                int idx = message.IndexOf(marker, start, StringComparison.Ordinal);
                if (idx > start && idx < next)
                {
                    next = idx;
                }
            }

            if (next == message.Length)
            {
                yield return message[start..].Trim();
                yield break;
            }

            yield return message[start..next].Trim();
            start = next + 1;
        }
    }

    private static void AppendFilterSegment(System.Text.StringBuilder sb, string segment, string prefix)
    {
        string body = segment.StartsWith(prefix, StringComparison.Ordinal)
            ? segment[prefix.Length..].Trim()
            : segment;
        if (body.StartsWith('['))
        {
            body = body.TrimStart('[').TrimEnd(']');
        }

        foreach (string token in body.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.AppendLine("  " + token);
        }
    }

    private static void AppendKeyValueLines(System.Text.StringBuilder sb, string indent, string segment)
    {
        foreach (string token in segment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.AppendLine(indent + token);
        }
    }

    private static string WrapLongLine(string text, int maxWidth)
    {
        if (text.Length <= maxWidth)
        {
            return text;
        }

        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            int len = Math.Min(maxWidth, text.Length - i);
            if (i + len < text.Length)
            {
                int breakAt = text.LastIndexOf(' ', i + len, len);
                if (breakAt > i)
                {
                    len = breakAt - i;
                }
            }

            sb.AppendLine(text.Substring(i, len).Trim());
            i += len;
            while (i < text.Length && text[i] == ' ')
            {
                i++;
            }
        }

        return sb.ToString().TrimEnd();
    }
}