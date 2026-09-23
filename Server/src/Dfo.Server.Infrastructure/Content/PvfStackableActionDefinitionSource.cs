using System.Globalization;
using Dfo.Pvf;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfStackableActionDefinitionSource(PvfArchiveProvider provider)
    : IStackableActionDefinitionSource
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, StackableActionDefinition?> cache = [];
    private IReadOnlyDictionary<int, string>? paths;

    public ValueTask<StackableActionDefinition?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (cache.TryGetValue(itemId, out var found)) return ValueTask.FromResult(found);
            var archive = provider.Archive;
            paths ??= PvfListFile.Read(archive, "stackable/stackable.lst")
                .ToDictionary(e => e.Id, e => "stackable/" + e.RelativePath);
            found = paths.TryGetValue(itemId, out var path) ? Read(archive, path) : null;
            cache[itemId] = found;
            return ValueTask.FromResult(found);
        }
    }

    private static StackableActionDefinition? Read(PvfArchive archive, string path)
    {
        var reader = archive.OpenScript(path);
        var unreadable = false;
        if (!PvfItemUseRuleDefinitionSource.TryReadFirstString(ref reader,
            archive.GetTag("[action type]"), out var action, ref unreadable)) return null;
        var min = PvfItemUseRuleDefinitionSource.FirstInt32(ref reader,
            archive.GetTag("[minimum level]"), ref unreadable);
        var maximumTag = archive.GetTag("[maximum level]");
        reader.Reset();
        var max = maximumTag.IsResolved && reader.TryMovePast(maximumTag)
            ? PvfItemUseRuleDefinitionSource.FirstInt32(ref reader, maximumTag, ref unreadable)
            : int.MaxValue;
        long expiry = 0;
        var expiryTag = archive.GetTag("[expiration date]");
        reader.Reset();
        if (expiryTag.IsResolved && reader.TryMovePast(expiryTag))
        {
            if (!reader.TryReadSectionValue(out var token)) unreadable = true;
            else if (token.IsString && DateTime.TryParseExact(archive.ResolveString(token.Value),
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                expiry = new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date)).ToUnixTimeSeconds();
            else if (token.Kind != Dfo.Pvf.Tokens.PvfTokenKind.Int32 || token.Value != 0)
                unreadable = true;
        }
        return new(archive.ResolveString(action.Value), min, max, expiry,
            !unreadable && min >= 0 && max >= min);
    }
}
