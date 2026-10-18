using Dfo.Pvf;
using Dfo.Pvf.Tokens;

internal sealed record SaoItemAudit(string Path, string? Name, string[] Expirations);
internal sealed record SaoDungeonAudit(string Path, string? Name, string? NameKind, string[] PlaceholderStrings, int[] MazeSize, string[] MazeGreed);
internal sealed record SaoContentAudit(SaoItemAudit[] Items, SaoDungeonAudit[] Dungeons, bool HasEntrance)
{
    public static SaoContentAudit Read(PvfArchive archive)
    {
        var items = new List<SaoItemAudit>();
        var dungeons = new List<SaoDungeonAudit>();
        for (var index = 0; index < archive.FileCount; index++)
        {
            var entry = archive.GetEntry(index);
            var folder = archive.ResolveString(entry.PathOffset);
            var name = archive.ResolveString(entry.NameOffset);
            var path = folder.Length == 0 ? name : folder + "/" + name;
            var item = (path.StartsWith("stackable/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("equipment/", StringComparison.OrdinalIgnoreCase))
                && (path.EndsWith(".stk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".equ", StringComparison.OrdinalIgnoreCase))
                && path.Contains("sao", StringComparison.OrdinalIgnoreCase);
            var dungeon = path.StartsWith("dungeon/towers/sao/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(".dgn", StringComparison.OrdinalIgnoreCase);
            if (!item && !dungeon) continue;
            var tokens = ReadTokens(archive, index);
            string? Value(string tag)
            {
                var offsets = Enumerable.Range(0, tokens.Count).Where(i => tokens[i].Kind == PvfTokenKind.Identifier
                    && archive.ResolveString(tokens[i].Value) == tag).ToArray();
                if (offsets.Length == 0) return null;
                if (offsets.Length != 1 || offsets[0] + 1 >= tokens.Count || !tokens[offsets[0] + 1].IsString)
                    throw new InvalidDataException($"Unexpected {tag} shape in {path}.");
                return archive.ResolveString(tokens[offsets[0] + 1].Value);
            }
            PvfToken[] ReadField(string tag)
            {
                var offset = tokens.FindIndex(x => x.Kind == PvfTokenKind.Identifier && archive.ResolveString(x.Value) == tag);
                return offset < 0 ? [] : tokens.Skip(offset + 1).TakeWhile(x => x.Kind != PvfTokenKind.Identifier).ToArray();
            }
            if (item)
            {
                var expirations = Enumerable.Range(0, tokens.Count).Where(i => tokens[i].Kind == PvfTokenKind.Identifier
                    && archive.ResolveString(tokens[i].Value) == "[expiration date]")
                    .Select(i => i + 1 < tokens.Count && tokens[i + 1].Kind == PvfTokenKind.QuotedString
                        ? archive.ResolveString(tokens[i + 1].Value)
                        : throw new InvalidDataException($"Unexpected expiration shape in {path}.")).ToArray();
                items.Add(new(path, Value("[name]"), expirations));
            }
            if (dungeon)
            {
                var nameOffset = tokens.FindIndex(x => x.Kind == PvfTokenKind.Identifier && archive.ResolveString(x.Value) == "[name]");
                dungeons.Add(new(path, Value("[name]"), nameOffset < 0 ? null : tokens[nameOffset + 1].Kind.ToString(),
                    tokens.Where(x => x.IsString).Select(x => archive.ResolveString(x.Value))
                        .Where(x => x.Contains("dnf_", StringComparison.OrdinalIgnoreCase)).ToArray(),
                    ReadField("[size]").Select(x => x.Kind == PvfTokenKind.Int32 ? x.Value : throw new InvalidDataException("Unexpected maze size.")).ToArray(),
                    ReadField("[greed]").Select(x => archive.ResolveString(x.Value)).ToArray()));
            }
        }
        var worldmap = ReadTokens(archive, archive.FindFile("worldmap/Towers.wdm"));
        return new(items.ToArray(), dungeons.ToArray(), worldmap.Any(x => x.Kind == PvfTokenKind.Int32 && x.Value == 3600));
    }

    private static List<PvfToken> ReadTokens(PvfArchive archive, int index)
    {
        if (index < 0 || !archive.GetEntry(index).IsScript) throw new InvalidDataException("Expected PVF script.");
        var result = new List<PvfToken>();
        var reader = archive.OpenScript(index);
        while (reader.Read(out var token)) result.Add(token);
        return result;
    }
}
