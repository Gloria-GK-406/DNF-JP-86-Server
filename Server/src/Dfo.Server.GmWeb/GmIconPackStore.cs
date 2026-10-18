using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Dfo.Server.GmWeb;

/// <summary>Reads item icons straight out of the deployed icon zip (built by Tool/GmIconPack); nothing is extracted.</summary>
/// <remarks>
/// The archive and its central directory stay open between requests and are reopened when the file's size or
/// last-write time changes. <see cref="ZipArchive"/> is not thread-safe, so every read holds the gate. The file is
/// shared for delete, so an operator can delete and replace it while the server runs.
/// </remarks>
internal sealed class GmIconPackStore(GmWebOptions options) : IDisposable
{
    internal const int MaxIconBytes = 64 * 1024;
    internal sealed record PackInfo(bool Available, int? Count, string? GeneratedAt, string? PvfSha256);
    private readonly Lock _gate = new();
    private ZipArchive? _archive;
    private (DateTime Written, long Length) _stamp;
    private PackInfo _info = new(false, null, null, null);

    internal byte[]? Icon(int itemId)
    {
        lock (_gate)
        {
            if (Open()?.GetEntry("icons/" + itemId.ToString(CultureInfo.InvariantCulture) + ".png") is not { Length: > 0 and <= MaxIconBytes } entry) return null;
            try
            {
                using var stream = entry.Open(); var bytes = new byte[entry.Length];
                stream.ReadExactly(bytes); return bytes;
            }
            catch (Exception error) when (error is InvalidDataException or IOException) { return null; }
        }
    }

    internal PackInfo Describe() { lock (_gate) { Open(); return _info; } }

    private ZipArchive? Open()
    {
        var file = new FileInfo(options.IconPack);
        if (options.IconPack.Length == 0 || !file.Exists) { Close(); return null; }
        var stamp = (file.LastWriteTimeUtc, file.Length);
        if (_archive is not null && stamp == _stamp) return _archive;
        Close();
        try
        {
            var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            try { _archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false); }
            catch { stream.Dispose(); throw; }
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException) { return null; }
        _stamp = stamp; _info = Manifest(_archive);
        return _archive;
    }

    // A pack without a readable manifest still serves icons; it only cannot describe itself.
    private static PackInfo Manifest(ZipArchive archive)
    {
        try
        {
            if (archive.GetEntry("manifest.json") is not { Length: > 0 and <= MaxIconBytes } entry) return new(true, null, null, null);
            using var stream = entry.Open(); using var document = JsonDocument.Parse(stream); var root = document.RootElement;
            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            int? count = root.TryGetProperty("count", out var number) && number.TryGetInt32(out var parsed) ? parsed : null;
            return new(true, count, Text("generatedAt"), Text("pvfSha256"));
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or IOException or InvalidOperationException) { return new(true, null, null, null); }
    }

    private void Close() { _archive?.Dispose(); _archive = null; _info = new(false, null, null, null); }
    public void Dispose() { lock (_gate) Close(); }
}
