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
    private readonly Dictionary<int, IReadOnlyList<string>> gemEffects = [];
    private IReadOnlyDictionary<int, HashSet<int>>? expertBeadCards;

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

    public ValueTask<IReadOnlyList<string>> GuardianGemEffectsAsync(int itemId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!gemEffects.TryGetValue(itemId, out var effects))
                gemEffects[itemId] = effects = ReadGuardianGemEffects(itemId);
            return ValueTask.FromResult(effects);
        }
    }

    private IReadOnlyList<string> ReadGuardianGemEffects(int itemId)
    {
        var archive = provider.Archive;
        StackablesIndex(archive);
        if (!stackables.TryGetValue(itemId, out var relative))
            return [];
        var sections = PvfDocument.TryOpen(archive, "stackable/" + relative, PvfPreamble.Ignore)?.Sections ?? [];
        var type = sections.FirstOrDefault(section => section.Name == "[stackable type]");
        if (type is not { Values.Count: > 0 } || Name(archive, type.Values[0]) != "flag gem")
            return [];
        var effects = sections.SkipWhile(section => section.Name != "[enchant]").Skip(1).ToArray();
        if (!effects.Any(section => section.Name == "[/enchant]"))
            return [];
        return effects.TakeWhile(section => section.Name != "[/enchant]").Select(section => section.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public ValueTask<bool> ValidateExpertCardAsync(int cardItemId, byte upgradeCount, int? equipmentItemId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
            return ValueTask.FromResult(ValidateExpertCard(provider.Archive, cardItemId, upgradeCount, equipmentItemId));
    }

    public ValueTask<EquipmentEnchantDefinition?> ResolveExpertBeadAsync(int beadItemId, byte upgradeCount,
        int? equipmentItemId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            var archive = provider.Archive;
            StackablesIndex(archive);
            if (!stackables.TryGetValue(beadItemId, out var path)
                || PvfDocument.TryOpen(archive, "stackable/" + path, PvfPreamble.Ignore)?.AtRoot() is not { } bead
                || !IsBead(archive, bead.Sections)
                || equipmentItemId is { } target && !AllowsBeadTarget(bead.Sections, target))
                return ValueTask.FromResult<EquipmentEnchantDefinition?>(null);
            expertBeadCards ??= PvfExpertJobDefinitionSource.ReadExpertBeadCards(archive);
            var declaredCard = PvfSections.SingleInt32(archive, bead.Sections, Fields.MonsterCardIdField,
                PvfRepeats.AsUndeclared, "equipment");
            return ValueTask.FromResult<EquipmentEnchantDefinition?>(
                declaredCard is { } card && expertBeadCards.TryGetValue(beadItemId, out var candidates) && candidates.Contains(card)
                && ValidateExpertCard(archive, card, upgradeCount, equipmentItemId)
                    ? new(beadItemId, card) : null);
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
        var cardId = PvfSections.SingleInt32(archive, bead, Fields.MonsterCardIdField, PvfRepeats.AsUndeclared, "equipment");
        if (!IsBead(archive, bead) || cardId is not > 0 || !stackables.TryGetValue(cardId.Value, out var cardPath)
            || !AllowsBeadTarget(bead, key.Target)) return null;

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

    private bool ValidateExpertCard(PvfArchive archive, int cardItemId, byte upgradeCount, int? equipmentItemId)
    {
        StackablesIndex(archive);
        if (!stackables.TryGetValue(cardItemId, out var path)
            || PvfDocument.TryOpen(archive, "stackable/" + path, PvfPreamble.Ignore) is not { } card)
            return false;
        var sections = card.AtRoot().Sections;
        var category = PvfSections.Single(sections, Fields.ItemCategoryField, PvfRepeats.AsUndeclared, "card");
        var categoryName = category is null ? "" : string.Join(' ', category.Values.Select(token => Name(archive, token)));
        var monsterCard = categoryName == "monster card" || categoryName.StartsWith("monster card ", StringComparison.Ordinal);
        var type = PvfSections.Single(sections, Fields.StackableTypeField, PvfRepeats.AsUndeclared, "card");
        var material = type?.Values is [var name, { Kind: PvfTokenKind.Int32, Value: 1 }]
            && Name(archive, name) == "material expert job";
        var allowed = PvfSections.Single(sections, Fields.StringDataField, PvfRepeats.AsUndeclared, "card");
        if (allowed is null || allowed.Values.Count < 2 || allowed.Values.Any(token => !token.IsString))
            return false;
        var indices = card.Sections.Where(section => section.Name == Fields.EnchantIndexField.Name).ToArray();
        if (!(monsterCard || material && indices.Length != 0)
            || (indices.Length == 0 ? upgradeCount != 0
                : indices.Any(section => section.Values is not [{ Kind: PvfTokenKind.Int32 }])
                  || !indices.Any(section => section.Values[0].Value == upgradeCount)))
            return false;
        if (equipmentItemId is not { } targetId) return true;
        EquipmentIndex(archive);
        if (!equipment.TryGetValue(targetId, out var targetPath)
            || PvfDocument.TryOpen(archive, "equipment/" + targetPath, PvfPreamble.Ignore)?.AtRoot() is not { } target)
            return false;
        var equipmentType = PvfSections.Single(target.Sections, Fields.EquipmentTypeField, PvfRepeats.AsUndeclared, "equipment");
        if (equipmentType is null || equipmentType.Values.Count is < 1 or > 2
            || !equipmentType.Values[0].IsString || equipmentType.Values.Count == 2
                && equipmentType.Values[1].Kind != PvfTokenKind.Int32)
            return false;
        var targetType = Name(archive, equipmentType.Values[0]);
        return targetType.Length > 0 && allowed.Values.Skip(1).Any(token => Name(archive, token) == targetType);
    }

    private static bool IsBead(PvfArchive archive, IReadOnlyList<PvfSection> bead)
    {
        var type = PvfSections.Single(bead, Fields.StackableTypeField, PvfRepeats.AsUndeclared, "equipment");
        return type?.Values is [var name, { Kind: PvfTokenKind.Int32, Value: 0 }] && Name(archive, name) == "enchant waste";
    }

    private static bool AllowsBeadTarget(IReadOnlyList<PvfSection> bead, int itemId)
    {
        foreach (var whitelist in new[] { Fields.TargetItemIdField, Fields.BeadLimitedUsableItemField })
        {
            var declarations = bead.Where(section => section.Name == whitelist.Name).ToArray();
            if (declarations.Length > 1 || declarations.Length == 1
                && (declarations[0].Values.Count == 0
                    || declarations[0].Values.Any(token => token.Kind != PvfTokenKind.Int32)
                    || !declarations[0].Values.Any(token => token.Value == itemId))) return false;
        }
        return true;
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
