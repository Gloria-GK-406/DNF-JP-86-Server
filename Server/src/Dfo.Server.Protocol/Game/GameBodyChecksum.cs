using System.Security.Cryptography;

namespace Dfo.Server.Protocol.Game;

internal static class GameBodyChecksum
{
    // A21 RVA 01034D10 -> 00026A20; compatibility checksum, not authentication.
    public static bool IsValid(ReadOnlySpan<byte> body, ReadOnlySpan<byte> checksum)
    {
        if (checksum.Length != 4) return false;
        Span<byte> md5 = stackalloc byte[16]; MD5.HashData(body, md5);
        return checksum[0] == (byte)(md5[0] ^ md5[13] ^ md5[8] ^ md5[10] ^ 0x81)
            && checksum[1] == (byte)(md5[1] ^ md5[9] ^ md5[5] ^ md5[12] ^ 0x78)
            && checksum[2] == (byte)(md5[2] ^ md5[6] ^ md5[14] ^ md5[4] ^ 0x1a)
            && checksum[3] == (byte)(md5[3] ^ md5[15] ^ md5[11] ^ md5[7] ^ 0xbf);
    }
}
