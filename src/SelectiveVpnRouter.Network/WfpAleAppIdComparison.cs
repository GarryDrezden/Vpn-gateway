using System.Globalization;
using System.Text;

namespace SelectiveVpnRouter.Network;

public sealed record WfpAleAppIdDiffDetail(
    int FwpmLength,
    int RuntimeLength,
    uint FwpmByteLength,
    uint RuntimeByteLength,
    int FirstDiffIndex,
    char? FwpmChar,
    int? FwpmCodePoint,
    char? RuntimeChar,
    int? RuntimeCodePoint,
    string Utf16LeHexWindow);

public static class WfpAleAppIdComparison
{
    public static bool EqualsOrdinal(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return false;
        }

        return string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
    }

    public static bool EqualsOrdinalIgnoreCase(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return false;
        }

        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    public static WfpAleAppIdDiffDetail Analyze(
        string fwpm,
        string runtime,
        uint fwpmByteLength = 0,
        uint runtimeByteLength = 0)
    {
        fwpm = Normalize(fwpm);
        runtime = Normalize(runtime);
        int fwpmLen = fwpm.Length;
        int runtimeLen = runtime.Length;
        int max = Math.Max(fwpmLen, runtimeLen);
        int firstDiff = -1;
        char? fwpmChar = null;
        char? runtimeChar = null;
        for (int i = 0; i < max; i++)
        {
            char f = i < fwpmLen ? fwpm[i] : '\0';
            char r = i < runtimeLen ? runtime[i] : '\0';
            if (f != r)
            {
                firstDiff = i;
                if (i < fwpmLen)
                {
                    fwpmChar = f;
                }

                if (i < runtimeLen)
                {
                    runtimeChar = r;
                }

                break;
            }
        }

        int? fwpmCp = fwpmChar is char fc ? fc : null;
        int? runtimeCp = runtimeChar is char rc ? rc : null;

        string hexWindow = firstDiff >= 0
            ? FormatUtf16LeHexWindow(fwpm, runtime, firstDiff)
            : "(no ordinal difference)";

        return new WfpAleAppIdDiffDetail(
            fwpmLen,
            runtimeLen,
            fwpmByteLength,
            runtimeByteLength,
            firstDiff,
            fwpmChar,
            fwpmCp,
            runtimeChar,
            runtimeCp,
            hexWindow);
    }

    public static string FormatDiffReport(WfpAleAppIdDiffDetail detail)
        => "fwpmLength=" + detail.FwpmLength
           + " runtimeLength=" + detail.RuntimeLength
           + " fwpmByteLength=" + detail.FwpmByteLength
           + " runtimeByteLength=" + detail.RuntimeByteLength
           + " firstDiffIndex=" + detail.FirstDiffIndex
           + " fwpmChar=" + FormatChar(detail.FwpmChar)
           + " fwpmCodePoint=" + FormatCodePoint(detail.FwpmCodePoint)
           + " runtimeChar=" + FormatChar(detail.RuntimeChar)
           + " runtimeCodePoint=" + FormatCodePoint(detail.RuntimeCodePoint)
           + " utf16leWindow=" + detail.Utf16LeHexWindow;

    private static string Normalize(string value) => value.TrimEnd('\0');

    private static string FormatChar(char? c)
        => c is null ? "(none)" : "'" + c + "'";

    private static string FormatCodePoint(int? cp)
        => cp is null ? "(none)" : "U+" + cp.Value.ToString("X4", CultureInfo.InvariantCulture);

    private static string FormatUtf16LeHexWindow(string fwpm, string runtime, int charIndex)
    {
        const int wcharRadius = 4;
        int start = Math.Max(0, charIndex - wcharRadius);
        int endFwpmExclusive = Math.Min(fwpm.Length, charIndex + wcharRadius + 1);
        int endRuntimeExclusive = Math.Min(runtime.Length, charIndex + wcharRadius + 1);
        var sb = new StringBuilder();
        sb.Append("fwpm[@").Append(start).Append("..").Append(endFwpmExclusive).Append("]=");
        sb.Append(HexUtf16Le(fwpm, start, endFwpmExclusive));
        sb.Append(" | runtime[@").Append(start).Append("..").Append(endRuntimeExclusive).Append("]=");
        sb.Append(HexUtf16Le(runtime, start, endRuntimeExclusive));
        return sb.ToString();
    }

    private static string HexUtf16Le(string text, int startChar, int endCharExclusive)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "(empty)";
        }

        startChar = Math.Clamp(startChar, 0, text.Length);
        endCharExclusive = Math.Clamp(endCharExclusive, startChar, text.Length);
        if (startChar >= endCharExclusive)
        {
            return "(empty)";
        }

        ReadOnlySpan<char> slice = text.AsSpan(startChar, endCharExclusive - startChar);
        var bytes = new byte[slice.Length * 2];
        Encoding.Unicode.GetBytes(slice, bytes);
        return string.Join(" ", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }
}