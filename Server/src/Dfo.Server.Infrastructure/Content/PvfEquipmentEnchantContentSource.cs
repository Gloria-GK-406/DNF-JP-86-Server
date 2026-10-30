using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEquipmentEnchantContentSource(PvfArchiveProvider provider)
    : IEquipmentEnchantContentSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<int>> TargetItemIdField = PvfField.Int32List("[target item id]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> BeadLimitedUsableItemField = PvfField.Int32List("[bead limited usable item]", optional: true);

        internal static readonly PvfField<IReadOnlyList<PvfToken>> StackableTypeField = PvfField.Tokens("[stackable type]", optional: true);
        internal static readonly PvfField<int> MonsterCardIdField = PvfField.Int32("[monster card id]", optional: true);
        internal static readonly PvfField<string> ItemCategoryField = PvfField.String("[item category]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> EquipmentTypeField = PvfField.Tokens("[equipment type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<string>> StringDataField = PvfField.Strings("[string data]", optional: true);
        internal static readonly PvfField<int> EnchantIndexField = PvfField.Int32("[enchant index]", optional: true);
    }

    private readonly Lock gate = new();
    private Dictionary<int, string>? stackables;
    private Dictionary<int, string>? equipment;
    private readonly Dictionary<(int Bead, int Target), EquipmentEnchantDefinition?> cache = [];

    // Explicit: the class already has a private Name(archive, token) helper.
    string Dfo.Server.Abstractions.Content.IContentWarmup.Name => "equipment-enchant-index";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var archive = provider.Archive;
            StackablesIndex(archive);
            EquipmentIndex(archive);
            return ValueTask.FromResult(stackables.Count + equipment.Count);
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(stackables))]
    private void StackablesIndex(Dfo.Pvf.PvfArchive archive)
    {
        stackables ??= Paths(archive, "stackable/stackable.lst");
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(equipment))]
    private void EquipmentIndex(Dfo.Pvf.PvfArchive archive)
    {
        equipment ??= Paths(archive, "equipment/equipment.lst");
    }

    public ValueTask<EquipmentEnchantDefinition?> ResolveAsync(
        int beadItemId, int equipmentItemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var key = (beadItemId, equipmentItemId);
            if (!cache.TryGetValue(key, out var result))
                cache[key] = result = Read(key);
            return ValueTask.FromResult(result);
        }
    }

    private EquipmentEnchantDefinition? Read((int Bead, int Target) key)
    {
        var archive = provider.Archive;
        StackablesIndex(archive);
        EquipmentIndex(archive);
        if (!stackables.TryGetValue(key.Bead, out var beadPath)
            || !equipment.TryGetValue(key.Target, out var targetPath)) return null;

        var bead = (PvfDocument.TryOpen(archive, "stackable/" + beadPath, PvfPreamble.Ignore)?.Sections ?? []);
        var type = PvfSections.Single(bead, Fields.StackableTypeField, PvfRepeats.AsUndeclared, "equipment");
        var cardId = PvfSections.SingleInt32(archive, bead, Fields.MonsterCardIdField, PvfRepeats.AsUndeclared, "equipment");
        if (type is null || type.Values.Count != 2
            || Name(archive, type.Values[0]) != "enchant waste"
            || type.Values[1].Kind != PvfTokenKind.Int32 || type.Values[1].Value != 0
            || cardId is not > 0 || !stackables.TryGetValue(cardId.Value, out var cardPath)) return null;

        foreach (var whitelist in new[] { Fields.TargetItemIdField, Fields.BeadLimitedUsableItemField })
        {
            var declarations = bead.Where(s => s.Name == whitelist.Name).ToArray();
            if (declarations.Length > 1 || declarations.Length == 1
                && (declarations[0].Values.Count == 0
                    || declarations[0].Values.Any(v => v.Kind != PvfTokenKind.Int32)
                    || !declarations[0].Values.Any(v => v.Value == key.Target))) return null;
        }

        var card = (PvfDocument.TryOpen(archive, "stackable/" + cardPath, PvfPreamble.Ignore)?.Sections ?? []);
        var category = PvfSections.Single(card, Fields.ItemCategoryField, PvfRepeats.AsUndeclared, "equipment");
        var target = (PvfDocument.TryOpen(archive, "equipment/" + targetPath, PvfPreamble.Ignore)?.Sections ?? []);
        var equipmentType = PvfSections.Single(target, Fields.EquipmentTypeField, PvfRepeats.AsUndeclared, "equipment");
        var stringData = PvfSections.Single(card, Fields.StringDataField, PvfRepeats.AsUndeclared, "equipment");
        if (category is not { Values.Count: 1 }
            || Name(archive, category.Values[0]) != "monster card"
            || equipmentType is null || equipmentType.Values.Count is < 1 or > 2
            || !equipmentType.Values[0].IsString
            || equipmentType.Values.Count == 2 && equipmentType.Values[1].Kind != PvfTokenKind.Int32
            || stringData is null || stringData.Values.Count < 2 || stringData.Values.Any(token => !token.IsString)
            || !HasSupportedEnchantIndex(card)) return null;
        var targetType = Name(archive, equipmentType.Values[0]);
        return targetType.Length > 0 && stringData.Values.Skip(1)
            .Any(token => Name(archive, token) == targetType)
            ? new(key.Bead, cardId.Value) : null;
    }

    private static Dictionary<int, string> Paths(PvfArchive archive, string path)
    {
        if (archive.FindFile(path) < 0) return [];
        try { return PvfListFile.Usable(archive, path); }
        catch (InvalidOperationException) { return []; }
    }

    internal static bool HasSupportedEnchantIndex(
        IReadOnlyList<PvfSection> sections)
    {
        var matches = sections.Where(section => section.Name == Fields.EnchantIndexField.Name).ToArray();
        return matches.Length == 1 && matches[0].Values.Count == 1
            && matches[0].Values[0].Kind == PvfTokenKind.Int32
            && matches[0].Values[0].Value == 0;
    }

    private static string Name(PvfArchive archive, PvfToken token) => token.IsString
        ? archive.ResolveString(token.Value).Trim().Trim('`').Trim().Trim('[', ']').ToLowerInvariant()
        : string.Empty;
}
