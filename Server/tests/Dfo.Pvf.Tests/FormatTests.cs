using System.Buffers.Binary;
using Dfo.Pvf.Codec;
using Dfo.Pvf.Container;
using Dfo.Pvf.Index;

namespace Dfo.Pvf.Tests;

[Trait("Category", "Unit")]
public sealed class HeaderTests
{
    private static byte[] BuildHeader(
        int fileCount = 2,
        int bodySize = 64,
        int chunkCount = 1,
        int hashTableSize = 16,
        int nameTableSize = 32,
        uint signature = PvfHeader.Signature)
    {
        var bytes = new byte[PvfHeader.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, signature);
        var fields = bytes.AsSpan(24);
        BinaryPrimitives.WriteInt32LittleEndian(fields, fileCount);
        BinaryPrimitives.WriteInt32LittleEndian(fields[8..], bodySize);
        BinaryPrimitives.WriteInt32LittleEndian(fields[12..], chunkCount);
        BinaryPrimitives.WriteInt32LittleEndian(fields[16..], hashTableSize);
        BinaryPrimitives.WriteInt32LittleEndian(fields[20..], nameTableSize);
        return bytes;
    }

    [Fact]
    public void Reads_declared_sections()
    {
        var header = PvfHeader.Read(BuildHeader(), archiveLength: 4096);

        Assert.Equal(2, header.FileCount);
        Assert.Equal(64, header.BodySize);
        Assert.Equal(1, header.ChunkCount);
        Assert.Equal(16, header.HashTableSize);
        Assert.Equal(32, header.NameTableSize);
    }

    [Fact]
    public void Section_layout_places_body_after_every_table()
    {
        var header = PvfHeader.Read(BuildHeader(), archiveLength: 4096);
        var layout = new SectionLayout(header);

        Assert.Equal(PvfHeader.Size, layout.FileTableOffset);
        Assert.Equal(2 * PvfHeader.FileEntrySize, layout.FileTableLength);
        Assert.Equal(layout.FileTableOffset + layout.FileTableLength, layout.HashTableOffset);
        Assert.Equal(layout.HashTableOffset + 16, layout.NameTableOffset);
        Assert.Equal(layout.NameTableOffset + 32, layout.ChunkTableOffset);
        Assert.Equal(layout.ChunkTableOffset + PvfHeader.ChunkEntrySize, layout.BodyOffset);
    }

    [Fact]
    public void Rejects_a_foreign_signature()
    {
        var bytes = BuildHeader(signature: 0xDEADBEEF);

        Assert.Throws<PvfFormatException>(() => PvfHeader.Read(bytes, archiveLength: 4096));
    }

    [Fact]
    public void Rejects_a_negative_section_size()
    {
        var bytes = BuildHeader(hashTableSize: -1);

        Assert.Throws<PvfFormatException>(() => PvfHeader.Read(bytes, archiveLength: 4096));
    }

    [Fact]
    public void Rejects_sections_that_run_past_the_file()
    {
        var bytes = BuildHeader(bodySize: 1_000_000);

        Assert.Throws<PvfFormatException>(() => PvfHeader.Read(bytes, archiveLength: 4096));
    }

    [Fact]
    public void Rejects_maximal_section_sizes_against_a_real_archive_length()
    {
        var bytes = BuildHeader(fileCount: int.MaxValue, bodySize: int.MaxValue, nameTableSize: int.MaxValue);

        Assert.Throws<PvfFormatException>(() => PvfHeader.Read(bytes, archiveLength: 64L * 1024 * 1024));
    }

    [Fact]
    public void Rejects_a_truncated_header()
    {
        Assert.Throws<PvfFormatException>(() => PvfHeader.Read(new byte[8], archiveLength: 4096));
    }
}

[Trait("Category", "Unit")]
public sealed class CipherTests
{
    // The cipher xors against a key-derived stream, so applying it twice restores the
    // input. That makes round-tripping a genuine check of the keystream, not a tautology:
    // a wrong stream still round-trips, but a stream that varies between the two passes
    // (as it would if state leaked across calls) does not.
    [Theory]
    [InlineData("HeaD", 0)]
    [InlineData("BodY", 1)]
    [InlineData("HASH", 4)]
    [InlineData("GRPI", 7)]
    [InlineData("BodY", 4096)]
    [InlineData("BodY", 4099)]
    public void Section_pass_is_self_inverse(string key, int length)
    {
        var original = new byte[length];
        Random.Shared.NextBytes(original);
        var buffer = original.ToArray();

        PvfCipher.DecryptSection(key, buffer);
        if (length > 0)
            Assert.False(buffer.AsSpan().SequenceEqual(original), "cipher left the buffer untouched");
        PvfCipher.DecryptSection(key, buffer);

        Assert.Equal(original, buffer);
    }

    [Theory]
    [InlineData("sTrA")]
    [InlineData("sTrW")]
    public void String_table_pass_is_self_inverse(string key)
    {
        var original = new byte[1023];
        Random.Shared.NextBytes(original);
        var buffer = original.ToArray();

        PvfCipher.DecryptStringTable(key, buffer);
        PvfCipher.DecryptStringTable(key, buffer);

        Assert.Equal(original, buffer);
    }

    [Fact]
    public void Section_and_string_table_use_different_streams()
    {
        var section = new byte[64];
        var stringTable = new byte[64];

        PvfCipher.DecryptSection("sTrA", section);
        PvfCipher.DecryptStringTable("sTrA", stringTable);

        Assert.NotEqual(section, stringTable);
    }

    [Fact]
    public void Rejects_a_key_shorter_than_the_format_allows()
    {
        Assert.Throws<ArgumentException>(() => PvfCipher.DecryptSection("Bod", new byte[8]));
    }
}

[Trait("Category", "Unit")]
public sealed class ZlibCodecTests
{
    private static byte[] Compress(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(
                   output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(payload);
        }

        return output.ToArray();
    }

    [Fact]
    public void Inflates_to_the_declared_length()
    {
        var payload = new byte[8192];
        Random.Shared.NextBytes(payload);
        var compressed = Compress(payload);

        var inflated = ZlibCodec.Inflate(compressed, 0, compressed.Length, payload.Length);

        Assert.Equal(payload, inflated);
    }

    [Fact]
    public void Inflates_without_a_declared_length()
    {
        var payload = new byte[3000];
        Random.Shared.NextBytes(payload);
        var compressed = Compress(payload);

        var inflated = ZlibCodec.InflateUnknownLength(compressed, 0, compressed.Length);

        Assert.Equal(payload, inflated);
    }

    [Fact]
    public void A_wrong_declared_length_fails_loudly_instead_of_truncating()
    {
        var payload = new byte[512];
        var compressed = Compress(payload);

        Assert.Throws<InvalidDataException>(
            () => ZlibCodec.Inflate(compressed, 0, compressed.Length, payload.Length + 1));
    }
}

[Trait("Category", "Unit")]
public sealed class ArchivePathTests
{
    [Theory]
    [InlineData("equipment/foo.equ", "equipment/foo.equ")]
    [InlineData(@"equipment\foo.equ", "equipment/foo.equ")]
    [InlineData("./equipment/foo.equ", "equipment/foo.equ")]
    [InlineData("/equipment/foo.equ", "equipment/foo.equ")]
    [InlineData("//./equipment/foo.equ", "equipment/foo.equ")]
    [InlineData("  equipment/foo.equ  ", "equipment/foo.equ")]
    [InlineData("equipment/", "equipment")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalizes_the_shapes_callers_actually_pass(string input, string expected) =>
        Assert.Equal(expected, FileIndex.NormalizePath(input));
}
