using System.Globalization;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEntryLifecycleContentSource(PvfArchiveProvider provider)
    : IEntryLifecycleContentSource
{
    private const string RefillPath = "etc/chn_server_limititemusageinfo.etc";
    private const string StackableListPath = "stackable/stackable.lst";
    private const string EquipmentListPath = "equipment/equipment.lst";

    public ValueTask<IReadOnlyList<DailyRefillRule>> GetDailyRefillRulesAsync(
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var archive = provider.Archive;
        var values = ReadSectionValues(archive, RefillPath, "[refill item]");
        var stackables = PvfListFile.Read(archive, StackableListPath).ToDictionary(value => value.Id);
        var equipment = PvfListFile.Read(archive, EquipmentListPath).Select(value => value.Id).ToHashSet();
        var parsed = Parse(values, preparedAt,
            id => ResolveItemMetadata(archive, id, stackables, equipment));
        return ValueTask.FromResult<IReadOnlyList<DailyRefillRule>>(parsed);
    }

    internal readonly record struct RefillItemMetadata(
        int StackLimit,
        byte ItemKind,
        int SlotStart,
        int SlotEnd);

    internal static IReadOnlyList<DailyRefillRule> Parse(
        IReadOnlyList<string> values,
        DateTimeOffset preparedAt,
        Func<int, RefillItemMetadata> metadata)
    {
        if (values.Count % 4 != 0)
            throw new FormatException($"PVF [{RefillPath}] [refill item] token count {values.Count} is not divisible by 4.");

        var nowBeijing = preparedAt.ToOffset(TimeSpan.FromHours(8));
        var result = new List<DailyRefillRule>();
        for (var index = 0; index < values.Count; index += 4)
        {
            if (!int.TryParse(values[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
                || !int.TryParse(values[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity)
                || !DateTime.TryParseExact(values[index + 2], "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresLocal)
                || !int.TryParse(values[index + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var modeValue))
                continue;
            var expiresAt = new DateTimeOffset(DateTime.SpecifyKind(expiresLocal, DateTimeKind.Unspecified), TimeSpan.FromHours(8));
            if (itemId <= 0 || quantity <= 0 || expiresAt <= nowBeijing
                || modeValue is < (int)DailyRefillMode.RefillToTarget
                    or > (int)DailyRefillMode.AddUpToStackLimit)
                continue;
            var item = metadata(itemId);
            result.Add(new DailyRefillRule(
                itemId, quantity, expiresAt, (DailyRefillMode)modeValue,
                item.StackLimit, item.ItemKind, item.SlotStart, item.SlotEnd));
        }
        return result;
    }

    private static IReadOnlyList<string> ReadSectionValues(PvfArchive archive, string path, string tag)
    {
        var section = archive.GetTag(tag);
        if (!section.IsResolved)
            throw new InvalidDataException($"PVF section {tag} is unavailable.");
        var values = new List<string>();
        var reader = archive.OpenScript(path);
        if (!reader.TryMovePast(section))
            throw new InvalidDataException($"PVF {path} does not contain {tag}.");
        while (reader.TryReadSectionValue(out var token))
        {
            values.Add(token.Kind == PvfTokenKind.Int32
                ? token.Value.ToString(CultureInfo.InvariantCulture)
                : token.IsString
                    ? archive.ResolveString(token.Value)
                    : throw new InvalidDataException($"PVF {path} {tag} contains unsupported token {token.Kind}."));
        }
        return values;
    }

    /// <summary>The same resolution, reachable by anything that has to place an item.</summary>
    internal static RefillItemMetadata ResolveItemMetadataFor(
        PvfArchive archive,
        int itemId,
        IReadOnlyDictionary<int, (int Id, string RelativePath)> stackables,
        IReadOnlySet<int> equipment) =>
        ResolveItemMetadata(archive, itemId, stackables, equipment);

    private static RefillItemMetadata ResolveItemMetadata(
        PvfArchive archive,
        int itemId,
        IReadOnlyDictionary<int, (int Id, string RelativePath)> stackables,
        IReadOnlySet<int> equipment)
    {
        if (!stackables.TryGetValue(itemId, out var entry))
            return equipment.Contains(itemId)
                ? new RefillItemMetadata(1, 1, 9, 64)
                : new RefillItemMetadata(0, 0, 0, -1);

        var limitTag = archive.GetTag("[stack limit]");
        var typeTag = archive.GetTag("[stackable type]");
        var reader = archive.OpenScript("stackable/" + entry.RelativePath);
        var limit = 0;
        if (limitTag.IsResolved && reader.TryMovePast(limitTag)
            && reader.TryReadSectionValue(out var limitToken)
            && limitToken.Kind == PvfTokenKind.Int32)
            limit = Math.Max(0, limitToken.Value);

        reader.Reset();
        string? stackableType = null;
        if (typeTag.IsResolved && reader.TryMovePast(typeTag)
            && reader.TryReadSectionValue(out var typeToken)
            && typeToken.IsString)
            stackableType = archive.ResolveString(typeToken.Value);

        var kind = ResolveStackableItemKind(itemId, stackableType);
        var range = ResolveSlotRange(kind, stackableType);
        return new RefillItemMetadata(limit, kind, range.Start, range.End);
    }

    internal static byte ResolveStackableItemKind(int itemId, string? stackableType)
    {
        var normalized = (stackableType ?? string.Empty).Replace("`", string.Empty).Trim();
        if (normalized.StartsWith("[", StringComparison.Ordinal))
        {
            var end = normalized.IndexOf(']', 1);
            if (end > 1)
                normalized = normalized[1..end];
        }
        normalized = normalized.Trim().ToLowerInvariant();

        if (normalized is "creature" or "feed") return 7;
        if (normalized == "avatar emblem") return 9;
        if (normalized == "material expert job") return 10;
        if (normalized == "quest") return 4;
        if (normalized == "material")
            return itemId is 3033 or 3034 or 3035 or 3036 or 3037 or 3262 ? (byte)11 : (byte)3;
        return 2;
    }

    internal static (int Start, int End) ResolveSlotRange(byte itemKind, string? stackableType)
    {
        var normalized = (stackableType ?? string.Empty).Replace("`", string.Empty).Trim();
        return itemKind switch
        {
            1 => (9, 64),
            3 or 11 when normalized.EndsWith("4", StringComparison.OrdinalIgnoreCase) => (345, 359),
            3 or 11 => (121, 176),
            4 => (177, 232),
            9 => (289, 344),
            10 => (233, 288),
            _ => (65, 120),
        };
    }
}
