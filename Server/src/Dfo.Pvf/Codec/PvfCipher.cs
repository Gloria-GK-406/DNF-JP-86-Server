using System.Buffers.Binary;

namespace Dfo.Pvf.Codec;

/// <summary>
/// The archive's linear-congruential stream cipher, applied in place over a span.
/// </summary>
/// <remarks>
/// Reverse-engineered from the client. The legacy implementation allocated a
/// four-byte array per word; writing through <see cref="BinaryPrimitives"/>
/// keeps the whole pass allocation-free.
/// </remarks>
public static class PvfCipher
{
    private const int SectionMagic = 0x269EC3;
    private const int StringTableMagic = 0x269EC9;
    private const int Multiplier = 0x343FD;

    /// <summary>Section key for the header, file table, hash table, chunk table and body.</summary>
    public static void DecryptSection(string key, Span<byte> buffer) =>
        Transform(key, buffer, SectionMagic);

    /// <summary>Variant key used by the sTrA and sTrW string buffers.</summary>
    public static void DecryptStringTable(string key, Span<byte> buffer) =>
        Transform(key, buffer, StringTableMagic);

    private static void Transform(string key, Span<byte> buffer, int magic)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (key.Length < 4)
            throw new ArgumentException("PVF cipher keys are four characters.", nameof(key));
        if (buffer.IsEmpty)
            return;

        unchecked
        {
            int k0 = (byte)key[0], k1 = (byte)key[1], k2 = (byte)key[2], k3 = (byte)key[3];
            var seed = (0x76826701 * k0) + (0x1C1 * (k3 + (0x1C1 * (k2 + (0x1C1 * k1)))));

            var wordCount = buffer.Length >> 2;
            for (var i = 0; i < wordCount; i++)
            {
                var intermediate = (Multiplier * seed) + magic;
                seed = (Multiplier * intermediate) + magic;

                var mask = (uint)(((seed >> 16) & 0xFFFF) + (intermediate & unchecked((int)0xFFFF0000)));
                var slot = buffer.Slice(i << 2, 4);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    slot,
                    BinaryPrimitives.ReadUInt32LittleEndian(slot) ^ mask);
            }

            var tail = buffer.Length - (wordCount << 2);
            if (tail == 0)
                return;

            var t1 = (Multiplier * seed) + magic;
            var t2 = (Multiplier * t1) + magic;
            var finalMask = (uint)((t1 & unchecked((int)0xFFFF0000)) + ((t2 >> 16) & 0xFFFF));

            Span<byte> maskBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(maskBytes, finalMask);

            var start = buffer.Length - tail;
            for (var i = 0; i < tail; i++)
                buffer[start + i] ^= maskBytes[i];
        }
    }
}
