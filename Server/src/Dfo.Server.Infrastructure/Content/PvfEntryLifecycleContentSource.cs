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
    private const string EquipmentDirectory = "equipment";
    private const string EquipmentTypeTag = "[equipment type]";

    /// <summary>The contract creature's own equipment type, as the archive spells it.</summary>
    private const string CreatureEquipmentType = "[creature]";

    /// <summary>The kind byte an ordinary worn piece carries.</summary>
    private const byte WornPieceItemKind = 1;

    /// <summary>The kind byte a contract creature carries.</summary>
    /// <remarks>
    /// The number storage has always recorded for the creature worn in slot 24, and the number the
    /// entry path reads a worn creature back by. It is named here because this is where the kind a
    /// template gets is decided, and the archive already knows the template is a creature.
    /// </remarks>
    private const byte CreatureItemKind = 5;

    /// <summary>
    /// What every avatar slot's name ends with: <c>[hat avatar]</c>, <c>[coat avatar]</c>, and the
    /// nine others.
    /// </summary>
    private const string AvatarEquipmentSuffix = "avatar]";

    public ValueTask<IReadOnlyList<DailyRefillRule>> GetDailyRefillRulesAsync(
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var archive = provider.Archive;
        var values = ReadSectionValues(archive, RefillPath, "[refill item]");
        var stackables = PvfListFile.Read(archive, StackableListPath).ToDictionary(value => value.Id);
        var equipment = ReadEquipmentPaths(archive);
        var parsed = Parse(values, preparedAt,
            id => ResolveItemMetadata(archive, id, stackables, equipment));
        return ValueTask.FromResult<IReadOnlyList<DailyRefillRule>>(parsed);
    }

    /// <summary>
    /// Every template the archive declares an individual piece, and the file that declares it.
    /// </summary>
    /// <remarks>
    /// The path is kept rather than only the identifier, because what sort of piece a template is
    /// is written inside that file and nowhere else. Names repeat across the list and the legacy
    /// index keeps the first, so a repeat is dropped rather than allowed to reassign the template.
    /// </remarks>
    internal static IReadOnlyDictionary<int, string> ReadEquipmentPaths(PvfArchive archive)
    {
        var paths = new Dictionary<int, string>();
        foreach (var (id, relativePath) in PvfListFile.Read(archive, EquipmentListPath))
            paths.TryAdd(id, relativePath);
        return paths;
    }

    internal readonly record struct RefillItemMetadata(
        int StackLimit,
        byte ItemKind,
        int SlotStart,
        int SlotEnd,
        ItemWorldClass WorldClass);

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
        IReadOnlyDictionary<int, string> equipment) =>
        ResolveItemMetadata(archive, itemId, stackables, equipment);

    private static RefillItemMetadata ResolveItemMetadata(
        PvfArchive archive,
        int itemId,
        IReadOnlyDictionary<int, (int Id, string RelativePath)> stackables,
        IReadOnlyDictionary<int, string> equipment)
    {
        if (!stackables.TryGetValue(itemId, out var entry))
            return equipment.TryGetValue(itemId, out var equipmentPath)
                ? WornPieceMetadata(ResolveWorldClass(archive, equipmentPath))
                : new RefillItemMetadata(0, 0, 0, -1, ItemWorldClass.Interchangeable);

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

        // Everything the archive stacks is a pile, the creature feed among them: feed is what a
        // creature eats, not a creature.
        return new RefillItemMetadata(
            limit, kind, range.Start, range.End, ItemWorldClass.Interchangeable);
    }

    /// <summary>
    /// What a template in the equipment index is: one of it per slot, in the equipment band, and
    /// of the kind its own <c>[equipment type]</c> makes it.
    /// </summary>
    /// <remarks>
    /// The kind used to be the constant 1 for everything the equipment index lists, the contract
    /// creature included -- while storage recorded the creature as kind 5 and the entry path read
    /// a worn creature back by that same 5. So the two sources answered differently about one
    /// template, and a creature that arrived through this reading was banked as ordinary gear that
    /// no reader of slot 24 would ever recognise as the creature standing in it. The archive is
    /// the one that knows: it spells the template <c>[creature]</c>, and the kind that says
    /// creature is 5.
    /// <para>
    /// <b>The avatar is deliberately left alone.</b> Storage records an avatar as kind 8 and this
    /// still answers 1 for one, so that pair still disagrees. It is the same defect and it is not
    /// this outcome's to fix; naming it here keeps the remaining disagreement visible rather than
    /// letting the creature's repair read as if every kind now agreed.
    /// </para>
    /// </remarks>
    private static RefillItemMetadata WornPieceMetadata(ItemWorldClass worldClass) =>
        new(1,
            worldClass == ItemWorldClass.Creature ? CreatureItemKind : WornPieceItemKind,
            9,
            64,
            worldClass);

    /// <summary>
    /// Which sort of individual piece a template is, out of its own <c>[equipment type]</c>.
    /// </summary>
    /// <remarks>
    /// The same tag <see cref="PvfEquipmentSlotDefinitionSource"/> reads to find the slot a piece
    /// is worn in, read here for the coarser question of what the piece is. One tag, one reading
    /// of it: the eleven names ending in <c>avatar</c> are the avatar slots, <c>[creature]</c> is
    /// the contract creature, and every other name -- weapon, coat, ring, title, artifact,
    /// charm, flag, name tag -- is ordinary worn gear.
    /// <para>
    /// A file that does not resolve, or declares no equipment type, is still an individual piece:
    /// it is in the equipment index, which is what says two of it can be told apart. Answering
    /// <see cref="ItemWorldClass.Interchangeable"/> there would turn a piece of gear into a pile
    /// on the strength of a tag being missing.
    /// </para>
    /// </remarks>
    private static ItemWorldClass ResolveWorldClass(PvfArchive archive, string relativePath)
    {
        var fileIndex = archive.FindFile(EquipmentDirectory + "/" + relativePath);
        if (fileIndex < 0)
            return ItemWorldClass.Equipment;

        var tag = archive.GetTag(EquipmentTypeTag);
        if (!tag.IsResolved)
            return ItemWorldClass.Equipment;

        var reader = archive.OpenScript(fileIndex);
        return reader.TryMovePast(tag)
            && reader.TryReadSectionValue(out var value)
            && value.IsString
            ? ClassifyEquipmentType(archive.ResolveString(value.Value))
            : ItemWorldClass.Equipment;
    }

    /// <summary>What an <c>[equipment type]</c> name says the piece is.</summary>
    internal static ItemWorldClass ClassifyEquipmentType(string? equipmentType)
    {
        var normalized = (equipmentType ?? string.Empty)
            .Replace("`", string.Empty).Trim().ToLowerInvariant();
        if (normalized == CreatureEquipmentType)
            return ItemWorldClass.Creature;
        return normalized.EndsWith(AvatarEquipmentSuffix, StringComparison.Ordinal)
            ? ItemWorldClass.Avatar
            : ItemWorldClass.Equipment;
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
