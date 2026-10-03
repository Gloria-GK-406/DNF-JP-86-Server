using Dfo.Pvf.Schema;
using System.Globalization;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEntryLifecycleContentSource(
    PvfArchiveProvider provider,
    IInventorySlotMappingRuleService? slotMappings = null)
    : IEntryLifecycleContentSource
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<PvfToken>> RefillItemField = PvfField.Tokens("[refill item]", optional: true);
        internal static readonly PvfField<int> StackLimitField = PvfField.Int32("[stack limit]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> StackableTypeField = PvfField.Tokens("[stackable type]", optional: true);
    }

    private readonly IInventorySlotMappingRuleService slotMappings =
        slotMappings ?? new InventorySlotMappingRuleService();
    private const string RefillPath = "etc/chn_server_limititemusageinfo.etc";
    private const string StackableListPath = "stackable/stackable.lst";
    private const string EquipmentListPath = "equipment/equipment.lst";
    private const string EquipmentDirectory = "equipment";
    private static readonly PvfField<IReadOnlyList<PvfToken>> EquipmentTypeTag = PvfField.Tokens("[equipment type]", optional: true);

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

    /// <summary>The kind byte an avatar carries.</summary>
    /// <remarks>
    /// The number storage has always recorded an avatar under, and the number every reader and
    /// writer of an avatar row looks for: the entry load will not hand out an avatar's six facts
    /// for a row of any other kind, the entry save refuses a row the archive calls an avatar that
    /// carries anything else, and both projections the client is served branch on this same 8. It
    /// is named here for the reason the creature's 5 is: this is where the kind a template gets is
    /// decided, and the archive already knows the template is an avatar.
    /// </remarks>
    private const byte AvatarItemKind = 8;

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
        var values = ReadSectionValues(archive, RefillPath, Fields.RefillItemField);
        var stackables = PvfListFile.Read(archive, StackableListPath).ToDictionary(value => value.Id);
        var equipment = ReadEquipmentPaths(archive);
        var parsed = Parse(values, preparedAt,
            id => ResolveItemMetadata(archive, id, stackables, equipment), slotMappings);
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
        Func<int, RefillItemMetadata> metadata,
        IInventorySlotMappingRuleService? slotMappings = null)
    {
        slotMappings ??= new InventorySlotMappingRuleService();
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
            if (item.SlotEnd < item.SlotStart)
                continue;
            var first = slotMappings.ToBusinessSlot(
                new GameSlot(InventoryContainerType.Main, item.SlotStart));
            var last = slotMappings.ToBusinessSlot(
                new GameSlot(InventoryContainerType.Main, item.SlotEnd));
            if (first.Space != last.Space)
                throw new InvalidDataException(
                    $"PVF daily refill Main/{item.SlotStart}..{item.SlotEnd} crosses business spaces.");
            result.Add(new DailyRefillRule(
                itemId, quantity, expiresAt, (DailyRefillMode)modeValue,
                item.StackLimit, item.ItemKind,
                new BusinessSlotRange(first.Space, first.Index, last.Index)));
        }
        return result;
    }

    private static IReadOnlyList<string> ReadSectionValues(PvfArchive archive, string path, PvfField<IReadOnlyList<PvfToken>> tag)
    {
        var document = PvfDocument.Open(archive, path);
        if (!document.TryRead(tag, out var tokens)) throw Invalid(path, tag, "required refill declaration is missing");
        if (tokens.Count % 4 != 0) throw Invalid(path, tag, "truncated refill tuple");
        var values = new List<string>();
        for (var offset = 0; offset < tokens.Count; offset += 4)
        {
            if (tokens[offset].Kind != PvfTokenKind.Int32 || tokens[offset + 1].Kind != PvfTokenKind.Int32
                || tokens[offset + 2].Kind != PvfTokenKind.QuotedString || tokens[offset + 3].Kind != PvfTokenKind.Int32)
                throw Invalid(path, tag, "expected item id, quantity, quoted expiration and mode");
            var expiration = archive.ResolveString(tokens[offset + 2].Value);
            if (!DateTime.TryParseExact(expiration, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw Invalid(path, tag, "invalid refill expiration");
            values.Add(tokens[offset].Value.ToString(CultureInfo.InvariantCulture));
            values.Add(tokens[offset + 1].Value.ToString(CultureInfo.InvariantCulture));
            values.Add(expiration);
            values.Add(tokens[offset + 3].Value.ToString(CultureInfo.InvariantCulture));
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
                ? WornPieceMetadata(ReadEquipmentType(archive, equipmentPath))
                : new RefillItemMetadata(0, 0, 0, -1, ItemWorldClass.Interchangeable);

        var path = "stackable/" + entry.RelativePath;
        var sections = RootSections(PvfDocument.Open(archive, path).Sections);
        var limit = TryRead(archive, sections, Fields.StackLimitField, path, out var declaredLimit) ? declaredLimit : 0;
        if (limit < 0) throw Invalid(path, Fields.StackLimitField, "negative stack limit");
        string? stackableType = TryRead(archive, sections, Fields.StackableTypeField, path, out var typeValues)
            ? TypeName(archive, typeValues, path, Fields.StackableTypeField) : null;

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
    /// The avatar was the second half of that same defect and is answered here too: storage records
    /// an avatar as kind 8, every reader and writer of an avatar row looks for that 8, and this
    /// gives an avatar template the 8 rather than the 1.
    /// </para>
    /// </remarks>
    private static RefillItemMetadata WornPieceMetadata(string? equipmentType)
    {
        var worldClass = ClassifyEquipmentType(equipmentType);
        var normalized = (equipmentType ?? string.Empty).Replace("`", string.Empty).Trim().ToLowerInvariant();
        var creatureEquipment = normalized is "[artifact red]" or "[artifact blue]" or "[artifact green]";
        var kind = creatureEquipment ? (byte)6 : worldClass switch
        {
            ItemWorldClass.Creature => CreatureItemKind,
            ItemWorldClass.Avatar => AvatarItemKind,
            _ => WornPieceItemKind,
        };
        return new(1, kind, 9, 64, worldClass);
    }

    private static string? ReadEquipmentType(PvfArchive archive, string relativePath)
    {
        var fileIndex = archive.FindFile(EquipmentDirectory + "/" + relativePath);
        if (fileIndex < 0)
            return null;

        var path = EquipmentDirectory + "/" + relativePath;
        var sections = RootSections(PvfDocument.Open(archive, path).Sections);
        return TryRead(archive, sections, EquipmentTypeTag, path, out var values)
            ? TypeName(archive, values, path, EquipmentTypeTag) : null;
    }

    private static string TypeName(PvfArchive archive, IReadOnlyList<PvfToken> values, string path, PvfField field)
    {
        // Stackable types carry a variable integer operand list (for example coupon 26 0).
        // Equipment type has only its optional single icon/sprite subtype.
        if (values.Count < 1 || !values[0].IsString
            || field != Fields.StackableTypeField && values.Count > 2
            || values.Skip(1).Any(value => value.Kind != PvfTokenKind.Int32 || value.Value < 0))
            throw Invalid(path, field, "expected a type name and complete nonnegative Int32 operands");
        var name = archive.ResolveString(values[0].Value);
        if (string.IsNullOrWhiteSpace(name)) throw Invalid(path, field, "empty type name");
        return name;
    }

    private static bool TryRead<T>(PvfArchive archive, IReadOnlyList<PvfSection> sections,
        PvfField<T> field, string path, out T value)
    {
        var found = sections.Where(section => field.Matches(section.Name)).ToArray();
        if (found.Length == 0) { value = default!; return false; }
        if (found.Length != 1) throw Invalid(path, field, "duplicate field");
        value = field.Decode(archive, found[0].Values, path);
        return true;
    }

    // The same names can legitimately occur inside a booster or another conditional block.
    private static IReadOnlyList<PvfSection> RootSections(IReadOnlyList<PvfSection> sections)
    {
        var closing = sections.Where(section => section.Name.StartsWith("[/", StringComparison.Ordinal))
            .Select(section => section.Name).ToHashSet(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var root = new List<PvfSection>();
        foreach (var section in sections)
        {
            if (stack.TryPeek(out var expected) && section.Name == expected) { stack.Pop(); continue; }
            if (stack.Count == 0) root.Add(section);
            var close = "[/" + section.Name[1..];
            if (closing.Contains(close)) stack.Push(close);
        }
        return root;
    }

    private static InvalidDataException Invalid(string path, PvfField field, string reason) =>
        new($"PVF '{path}', field '{field}': {reason}.");

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
