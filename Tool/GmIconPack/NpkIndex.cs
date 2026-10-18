using System.Buffers.Binary;
using System.Text;

namespace GmIconPack;

/// <summary>Where one IMG lives: the NPK file and the byte range inside it.</summary>
internal readonly record struct NpkEntry(string PackPath, int Offset, int Size);

/// <summary>
/// Name -> location index over a folder of NPK packs, built from the directory headers only.
/// Header: "NeoplePack_Bill\0", i32 count, then count x (i32 offset, i32 size, 256-byte name XOR <see cref="Key"/>).
/// </summary>
internal sealed class NpkIndex
{
    internal static readonly byte[] Magic = "NeoplePack_Bill\0"u8.ToArray();
    internal const int NameLength = 256;

    /// <summary>The fixed name key: the phrase, padded with "DNF" to 255 bytes, then a zero.</summary>
    internal static readonly byte[] Key = BuildKey();

    private static readonly Encoding NameEncoding;
    private readonly Dictionary<string, NpkEntry> entries = new(StringComparer.Ordinal);

    static NpkIndex()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        NameEncoding = Encoding.GetEncoding(949);
    }

    public int Count => entries.Count;
    public int PackCount { get; private set; }

    /// <summary>Indexes every *.npk in <paramref name="directory"/>, in ordinal file-name order; the first pack to name an IMG wins.</summary>
    public static NpkIndex Build(string directory)
    {
        var index = new NpkIndex();
        foreach (var path in Directory.EnumerateFiles(directory, "*.npk", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
            index.AddPack(path, stream);
        }
        return index;
    }

    /// <summary>Reads one pack's directory. Returns false (and adds nothing) when the magic does not match.</summary>
    public bool AddPack(string packPath, Stream stream)
    {
        var head = new byte[Magic.Length + 4];
        if (stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) != head.Length || !head.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            return false;
        var count = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(Magic.Length));
        if (count < 0) throw new InvalidDataException($"{packPath}: negative entry count {count}");

        var row = new byte[8 + NameLength];
        Span<byte> name = stackalloc byte[NameLength];
        for (var i = 0; i < count; i++)
        {
            stream.ReadExactly(row);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(row);
            var size = BinaryPrimitives.ReadInt32LittleEndian(row.AsSpan(4));
            for (var k = 0; k < NameLength; k++) name[k] = (byte)(row[8 + k] ^ Key[k]);
            var end = name.IndexOf((byte)0);
            var text = NameEncoding.GetString(end < 0 ? name : name[..end]).ToLowerInvariant();
            entries.TryAdd(text, new(packPath, offset, size));
        }
        PackCount++;
        return true;
    }

    /// <summary>PVF [icon] paths are relative to sprite/ ("Item/weapon/x.img" -> "sprite/item/weapon/x.img").</summary>
    public static string NormalizeImgPath(string imgPath)
    {
        var path = imgPath.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        return path.StartsWith("sprite/", StringComparison.Ordinal) ? path : "sprite/" + path;
    }

    public bool TryFind(string imgPath, out NpkEntry entry) => entries.TryGetValue(NormalizeImgPath(imgPath), out entry);

    /// <summary>The IMG bytes for <paramref name="imgPath"/>, or null when no pack names it.</summary>
    public byte[]? ReadImg(string imgPath)
    {
        if (!TryFind(imgPath, out var entry)) return null;
        using var stream = new FileStream(entry.PackPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
        if (entry.Offset < 0 || entry.Size < 0 || (long)entry.Offset + entry.Size > stream.Length)
            throw new ImgException(IconFailure.Malformed, $"{entry.PackPath}: entry {imgPath} [{entry.Offset}+{entry.Size}] runs past the pack end");
        stream.Position = entry.Offset;
        var blob = new byte[entry.Size];
        stream.ReadExactly(blob);
        return blob;
    }

    private static byte[] BuildKey()
    {
        var phrase = "puchikon@neople dungeon and fighter "u8;
        var key = new byte[NameLength];
        phrase.CopyTo(key);
        for (var i = phrase.Length; i < NameLength - 1; i++) key[i] = "DNF"u8[(i - phrase.Length) % 3];
        return key;
    }
}
