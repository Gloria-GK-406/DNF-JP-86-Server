namespace GmIconPack;

/// <summary>CRC-32 (IEEE 802.3, reflected 0xEDB88320) as PNG chunks use it.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data) => Append(0, data);

    /// <summary>Continues a finished CRC over more bytes, so a chunk's type and body need no concatenation.</summary>
    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        var c = ~crc;
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return ~c;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
