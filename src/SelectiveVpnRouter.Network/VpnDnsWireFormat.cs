using System.Net;
using System.Text;

namespace SelectiveVpnRouter.Network;

internal static class VpnDnsWireFormat
{
    internal const int DefaultUdpBufferSize = 512;

    internal static bool TryValidateHostname(string host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > VpnInterfaceDnsResolver.MaxHostLength)
            return false;

        ReadOnlySpan<char> span = host.AsSpan();
        int labelLength = 0;
        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            if (c == '.')
            {
                if (labelLength == 0)
                    return false;
                labelLength = 0;
                continue;
            }

            if (c > 127)
                return false;

            labelLength++;
            if (labelLength > 63)
                return false;
        }

        return labelLength > 0;
    }

    internal static byte[] BuildAQuery(string host, ushort transactionId)
    {
        string[] labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        using var ms = new MemoryStream(256);
        WriteUInt16(ms, transactionId);
        ms.WriteByte(0x01); // RD=1
        ms.WriteByte(0x00);
        ms.WriteByte(0x00);
        ms.WriteByte(0x01); // QDCOUNT=1
        ms.WriteByte(0x00);
        ms.WriteByte(0x00);
        ms.WriteByte(0x00);
        ms.WriteByte(0x00);
        ms.WriteByte(0x00);
        ms.WriteByte(0x00);
        foreach (string label in labels)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(label);
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes);
        }

        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(1); // TYPE A
        ms.WriteByte(0);
        ms.WriteByte(1); // CLASS IN
        return ms.ToArray();
    }

    internal static VpnDnsWireParseResult TryParseARecords(ReadOnlySpan<byte> response, ushort expectedTransactionId)
    {
        if (response.Length < 12)
            return VpnDnsWireParseResult.Malformed;

        ushort id = ReadUInt16(response, 0);
        if (id != expectedTransactionId)
            return VpnDnsWireParseResult.TransactionIdMismatch;

        ushort flags = ReadUInt16(response, 2);
        if ((flags & 0x8000) == 0)
            return VpnDnsWireParseResult.Malformed;

        if ((flags & 0x0200) != 0)
            return VpnDnsWireParseResult.Truncated;

        int rcode = flags & 0x000F;
        if (rcode == 3)
            return VpnDnsWireParseResult.NxDomain;
        if (rcode != 0)
            return rcode == 2 ? VpnDnsWireParseResult.ServFail : VpnDnsWireParseResult.ServFail;

        int qdCount = ReadUInt16(response, 4);
        int anCount = ReadUInt16(response, 6);
        int offset = 12;
        for (int q = 0; q < qdCount; q++)
        {
            if (!SkipName(response, ref offset))
                return VpnDnsWireParseResult.Malformed;
            if (offset + 4 > response.Length)
                return VpnDnsWireParseResult.Malformed;
            offset += 4;
        }

        var addresses = new List<IPAddress>();
        for (int i = 0; i < anCount; i++)
        {
            if (!SkipName(response, ref offset))
                return VpnDnsWireParseResult.Malformed;
            if (offset + 10 > response.Length)
                return VpnDnsWireParseResult.Malformed;

            int type = ReadUInt16(response, offset);
            int rdLength = ReadUInt16(response, offset + 8);
            offset += 10;
            if (offset + rdLength > response.Length)
                return VpnDnsWireParseResult.Malformed;

            if (type == 1 && rdLength == 4)
                addresses.Add(new IPAddress(response.Slice(offset, 4).ToArray()));

            offset += rdLength;
        }

        if (addresses.Count == 0)
            return anCount == 0 && rcode == 0
                ? VpnDnsWireParseResult.NoMatchingAnswer
                : VpnDnsWireParseResult.NoMatchingAnswer;

        return VpnDnsWireParseResult.Success(addresses);
    }

    internal static bool SkipName(ReadOnlySpan<byte> buf, ref int offset)
    {
        int jumps = 0;
        while (offset < buf.Length)
        {
            int len = buf[offset++];
            if (len == 0)
                return true;

            if ((len & 0xC0) == 0xC0)
            {
                if (offset >= buf.Length)
                    return false;
                if (++jumps > 8)
                    return false;
                offset++;
                return true;
            }

            if (len > 63)
                return false;

            offset += len;
            if (offset > buf.Length)
                return false;
        }

        return false;
    }

    internal static byte[] BuildResponse(
        ushort transactionId,
        ushort flags,
        int anCount,
        ReadOnlySpan<byte> answerRdata)
    {
        var ms = new MemoryStream(64);
        WriteUInt16(ms, transactionId);
        WriteUInt16(ms, flags);
        WriteUInt16(ms, 1); // QDCOUNT
        WriteUInt16(ms, (ushort)anCount);
        WriteUInt16(ms, 0);
        WriteUInt16(ms, 0);
        ms.WriteByte(3);
        ms.WriteByte((byte)'w');
        ms.WriteByte((byte)'w');
        ms.WriteByte((byte)'w');
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(1);
        ms.WriteByte(0);
        ms.WriteByte(1);
        if (anCount > 0)
        {
            ms.WriteByte(0xC0);
            ms.WriteByte(0x0C);
            ms.WriteByte(0x00);
            ms.WriteByte(0x01);
            ms.WriteByte(0x00);
            ms.WriteByte(0x01);
            ms.WriteByte(0x00);
            ms.WriteByte(0x00);
            ms.WriteByte(0x00);
            ms.WriteByte(0x3C);
            ms.WriteByte(0x00);
            ms.WriteByte(4);
            ms.Write(answerRdata.ToArray());
        }

        return ms.ToArray();
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> buf, int offset) =>
        (ushort)((buf[offset] << 8) | buf[offset + 1]);

    private static void WriteUInt16(Stream ms, ushort value)
    {
        ms.WriteByte((byte)(value >> 8));
        ms.WriteByte((byte)(value & 0xFF));
    }
}

internal readonly struct VpnDnsWireParseResult
{
    private VpnDnsWireParseResult(VpnDnsWireParseStatus status, IReadOnlyList<IPAddress>? addresses)
    {
        Status = status;
        Addresses = addresses ?? Array.Empty<IPAddress>();
    }

    public VpnDnsWireParseStatus Status { get; }

    public IReadOnlyList<IPAddress> Addresses { get; }

    public static VpnDnsWireParseResult Malformed => new(VpnDnsWireParseStatus.Malformed, null);

    public static VpnDnsWireParseResult TransactionIdMismatch =>
        new(VpnDnsWireParseStatus.TransactionIdMismatch, null);

    public static VpnDnsWireParseResult Truncated => new(VpnDnsWireParseStatus.Truncated, null);

    public static VpnDnsWireParseResult NxDomain => new(VpnDnsWireParseStatus.NxDomain, null);

    public static VpnDnsWireParseResult ServFail => new(VpnDnsWireParseStatus.ServFail, null);

    public static VpnDnsWireParseResult NoMatchingAnswer =>
        new(VpnDnsWireParseStatus.NoMatchingAnswer, null);

    public static VpnDnsWireParseResult Success(IReadOnlyList<IPAddress> addresses) =>
        new(VpnDnsWireParseStatus.Success, addresses);
}

internal enum VpnDnsWireParseStatus
{
    Success,
    Malformed,
    TransactionIdMismatch,
    Truncated,
    NxDomain,
    ServFail,
    NoMatchingAnswer,
}
