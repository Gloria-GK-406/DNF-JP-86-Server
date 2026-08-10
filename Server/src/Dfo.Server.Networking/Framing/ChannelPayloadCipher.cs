using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Dfo.Server.Networking.Framing;

public static class ChannelPayloadCipher
{
    public static byte[] Encrypt(
        ReadOnlySpan<byte> plaintext,
        string key,
        bool compress)
    {
        ArgumentNullException.ThrowIfNull(key);
        var padded = ZeroPad(plaintext, 16);
        var keyBytes = new byte[16];
        var encodedKey = Encoding.UTF8.GetBytes(key);
        encodedKey.AsSpan(0, Math.Min(encodedKey.Length, keyBytes.Length)).CopyTo(keyBytes);

        using var aes = Aes.Create();
        aes.Key = keyBytes;
        var encrypted = aes.EncryptEcb(padded, PaddingMode.None);
        return compress ? CompressZlib(encrypted) : encrypted;
    }

    private static byte[] ZeroPad(ReadOnlySpan<byte> data, int alignment)
    {
        var paddedLength = checked((data.Length + alignment - 1) / alignment * alignment);
        var result = new byte[paddedLength];
        data.CopyTo(result);
        return result;
    }

    private static byte[] CompressZlib(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return [];
        }

        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, ComputeAdler32(data));
        output.Write(checksum);
        return output.ToArray();
    }

    private static uint ComputeAdler32(ReadOnlySpan<byte> data)
    {
        const uint modulus = 65521;
        uint a = 1;
        uint b = 0;
        foreach (var value in data)
        {
            a = (a + value) % modulus;
            b = (b + a) % modulus;
        }

        return (b << 16) | a;
    }
}
