using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace GmIconPack;

/// <summary>Outcome counts. Missing reasons include those the PVF scan already decided.</summary>
internal sealed record IconPackSummary(int Candidates, int Resolved, IReadOnlyDictionary<IconFailure, int> Missing,
    int UniqueFrames, int NonStandardSizeItems, int NonStandardSizeFrames, IReadOnlyDictionary<int, int> ImgVersions,
    IReadOnlyDictionary<IconFailure, List<string>> Examples);

/// <summary>
/// Writes the pack: icons/{itemId}.png per resolved item plus manifest.json at the root. The entry names and the
/// manifest's camelCase keys are a contract with the server, which reads the zip in place.
/// </summary>
internal static class IconPackBuilder
{
    public const int IconSize = 28;
    public const string ManifestEntry = "manifest.json";
    private const int ExampleLimit = 3;
    public static string IconEntry(int itemId) => $"icons/{itemId.ToString(CultureInfo.InvariantCulture)}.png";

    /// <param name="readImg">IMG bytes for a PVF [icon] path, or null when no pack has it.</param>
    public static IconPackSummary Build(IEnumerable<IconRequest> requests, Func<string, byte[]?> readImg,
        Stream output, string pvfSha256, DateTimeOffset generatedAt)
    {
        var missing = new SortedDictionary<IconFailure, int>();
        var versions = new SortedDictionary<int, int>();
        var examples = new Dictionary<IconFailure, List<string>>();
        void Miss(IconRequest request, IconFailure reason, string? detail = null)
        {
            missing[reason] = missing.GetValueOrDefault(reason) + 1;
            if (!examples.TryGetValue(reason, out var list)) examples[reason] = list = [];
            if (list.Count < ExampleLimit) list.Add($"{request.ItemId} {request.Img} #{request.Frame}{(detail is null ? "" : ": " + detail)}");
        }
        int candidates = 0, resolved = 0, uniqueFrames = 0, oddItems = 0, oddFrames = 0;

        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Group by IMG so each IMG is read and parsed once; frames are then encoded once per (img, frame).
            var byImg = new Dictionary<string, List<IconRequest>>(StringComparer.Ordinal);
            foreach (var request in requests)
            {
                candidates++;
                if (request.Failure is { } early) { Miss(request, early); continue; }
                var key = NpkIndex.NormalizeImgPath(request.Img!);
                if (!byImg.TryGetValue(key, out var list)) byImg[key] = list = [];
                list.Add(request);
            }

            foreach (var group in byImg.Values)
            {
                ImgFile img;
                try
                {
                    var blob = readImg(group[0].Img!);
                    if (blob is null) { foreach (var r in group) Miss(r, IconFailure.ImgNotFound); continue; }
                    img = ImgFile.Parse(blob);
                }
                catch (ImgException e) { foreach (var r in group) Miss(r, e.Reason, e.Message); continue; }
                versions[img.Version] = versions.GetValueOrDefault(img.Version) + 1;

                // Keyed by the frame that owns the pixels, so links to one picture share one encoding.
                var frames = new Dictionary<int, (byte[]? Png, ImgException? Failure, bool Odd)>();
                foreach (var request in group.OrderBy(r => r.Frame).ThenBy(r => r.ItemId))
                {
                    (byte[]? Png, ImgException? Failure, bool Odd) encoded;
                    try
                    {
                        var owner = img.ResolveLink(request.Frame);
                        if (!frames.TryGetValue(owner, out encoded))
                        {
                            try
                            {
                                var frame = img.Decode(owner);
                                encoded = (PngEncoder.Encode(frame.Width, frame.Height, frame.Rgba), null, frame.Width != IconSize || frame.Height != IconSize);
                                uniqueFrames++;
                                if (encoded.Odd) oddFrames++;
                            }
                            catch (ImgException e) { encoded = (null, e, false); }
                            frames[owner] = encoded;
                        }
                    }
                    catch (ImgException e) { encoded = (null, e, false); }
                    if (encoded.Png is null) { Miss(request, encoded.Failure!.Reason, encoded.Failure.Message); continue; }
                    // PNG is already deflated; storing avoids paying for a second pass that gains nothing.
                    using (var entry = zip.CreateEntry(IconEntry(request.ItemId), CompressionLevel.NoCompression).Open())
                        entry.Write(encoded.Png);
                    resolved++;
                    if (encoded.Odd) oddItems++;
                }
            }

            using var manifest = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal).Open();
            WriteManifest(manifest, generatedAt, pvfSha256, resolved);
        }
        return new(candidates, resolved, missing, uniqueFrames, oddItems, oddFrames, versions, examples);
    }

    internal static void WriteManifest(Stream stream, DateTimeOffset generatedAt, string pvfSha256, int count)
    {
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteString("generatedAt", generatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        json.WriteString("pvfSha256", pvfSha256);
        json.WriteNumber("count", count);
        json.WriteEndObject();
    }
}
