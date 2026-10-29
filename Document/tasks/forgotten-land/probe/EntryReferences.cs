using Dfo.Pvf;
using Dfo.Pvf.Tokens;

internal static class EntryReferences
{
    // Bounded context only. Numeric matches are candidates, not entry proof.
    public static IReadOnlyList<object> Read(string path)
    {
        using var archive = PvfArchive.Open(path, new PvfArchiveOptions { ChunkCacheBytes = 16 * 1024 * 1024 });
        var hits = new List<object>();
        for (var index = 0; index < archive.FileCount; index++)
        {
            var entry = archive.GetEntry(index);
            if (!entry.IsScript) continue;
            var fileName = archive.ResolveString(entry.NameOffset);
            if (Path.GetExtension(fileName).ToLowerInvariant() is not (".wdm" or ".evt")) continue;
            var folder = archive.ResolveString(entry.PathOffset);
            var previous = new Queue<string>();
            var context = new List<string>();
            var matches = new List<string>();
            var remaining = 0;
            var script = archive.OpenScript(index);
            while (script.Read(out var token))
            {
                var value = token.IsString ? archive.ResolveString(token.Value) : token.Value.ToString();
                var hit = token.Kind == PvfTokenKind.Int32 && token.Value == 4000
                    || token.IsString && (value.Equals("lost land", StringComparison.OrdinalIgnoreCase)
                        || value.Equals("lostland", StringComparison.OrdinalIgnoreCase));
                if (hit)
                {
                    matches.Add(string.Join(" | ", previous) + " | >>>" + value + "<<<");
                    remaining = 6;
                }
                else if (remaining > 0)
                {
                    context.Add(value);
                    remaining--;
                    if (remaining == 0) { matches.Add("following: " + string.Join(" | ", context)); context.Clear(); }
                }
                previous.Enqueue(value);
                if (previous.Count > 6) previous.Dequeue();
            }
            if (matches.Count == 0) continue;
            hits.Add(new { Path = folder + "/" + fileName, Matches = matches });
            if (hits.Count > 200) throw new InvalidDataException("Entry reference hit budget exceeded.");
        }
        return hits;
    }
}
