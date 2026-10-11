using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Quests;

namespace Dfo.Server.Application.Characters;

internal sealed class SelectCharacterQuestEntryService(
    IQuestContentRuleService quests,
    ICharacterQuestLogDomainService logs) : ISelectCharacterQuestEntryDomainService
{
    public async ValueTask<SelectCharacterQuestSynchronization?> SynchronizeAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken)
    {
        var planned = new List<QuestTriggerUpdate>();
        foreach (var active in result.CharacterEntry.Quests.ActiveQuests.OrderBy(value => value.Slot))
        {
            var definition = await quests.FindAsync(active.QuestId, cancellationToken).ConfigureAwait(false);
            if (definition is null) continue;
            var after = QuestKillObjectiveRules.RepairRegionalResidual(definition.KillObjectives, active.TriggerValue);
            if (definition.SeekingItems is { Count: > 0 } requirements)
            {
                long missing = 0;
                foreach (var requirement in requirements)
                    missing += Math.Max(0, requirement.RequiredCount - Count(result, requirement.ItemId));
                after = ReplaceChannel0(after, missing);
            }
            if (after != active.TriggerValue)
                planned.Add(new QuestTriggerUpdate(active.Slot, active.QuestId, active.TriggerValue, after));
        }
        if (planned.Count == 0) return null;
        // Each write carries the value this entry snapshot was built from, so a slot that has
        // moved since fails rather than being overwritten with a decision made about the old one.
        var log = logs.LogOf(session.AccountId, session.CharacterId);
        var committed = new List<QuestTriggerUpdate>();
        foreach (var update in planned.OrderBy(value => value.Slot))
        {
            if (update.After == update.Before) continue;
            // A quest the snapshot said was being carried and the log no longer holds is the same
            // situation the write's own guard exists for, and it fails the same way: entry stops
            // rather than reporting a correction that was never made.
            if (!await log.RecordProgressAsync(
                    new CarriedQuest(update.Slot, update.QuestId, update.Before),
                    update.After,
                    cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new InvalidDataException(
                    $"Quest {update.QuestId} changed concurrently during entry synchronization.");
            }

            committed.Add(update);
        }
        if (committed.Count == 0) return null;
        var bySlot = result.CharacterEntry.Quests.ActiveQuests.ToDictionary(value => value.Slot);
        foreach (var change in committed)
            bySlot[change.Slot] = bySlot[change.Slot] with { TriggerValue = change.After };
        var updatedQuests = result.CharacterEntry.Quests with
        {
            ActiveQuests = bySlot.Values.OrderBy(value => value.Slot).ToArray()
        };
        var updated = result with
        {
            CharacterEntry = result.CharacterEntry with { Quests = updatedQuests }
        };
        return new SelectCharacterQuestSynchronization(updated,
            new AcceptedQuestSynchronizationState(bySlot.Values.OrderBy(value => value.Slot)
            .Select(value => new AcceptedQuestProgressState(
                checked((ushort)value.QuestId), unchecked((uint)value.TriggerValue))).ToArray()));
    }

    internal static int ReplaceChannel0(int trigger, long value) =>
        (trigger & ~0x1FF) | (int)Math.Clamp(value, 0, 0x1FF);

    private static int Count(SelectCharacterResult result, int itemId)
    {
        // Shared materials belong to the account, not the character bag. Missing entries in
        // this sparse projection mean zero; legacy bag copies must not count a second time.
        if (AccountMaterialCatalog.ContainsItem(itemId))
            return Math.Max(0, result.AccountInventory.VirtualResources
                .SingleOrDefault(value => value.ItemId == itemId)?.Value ?? 0);
        var inventory = result.CharacterInventory;
        var virtualValue = inventory.VirtualResources.SingleOrDefault(value => value.ItemId == itemId);
        if (virtualValue is not null) return Math.Max(0, virtualValue.Value);
        long total = inventory.Containers.Where(value => value.ContainerType == InventoryContainerType.Main)
            .SelectMany(value => value.Items).Where(value => value.Core.ItemId == itemId)
            .Sum(value => value.Core.ItemKind is 2 or 3 or 4 or 7 or 9 or 10 or 11
                ? Math.Max(0, value.Core.Value) : 1);
        return checked((int)Math.Min(int.MaxValue, total));
    }
}

/// <remarks>
/// The equipped item and its creature state settle in one database transaction.
/// </remarks>
internal sealed class SelectCharacterPetEntryService(
    ICharacterDomainStore characters,
    IItemDomainStore items,
    AmbientStoreAccess stores,
    ICreatureDomainService? creatureDomains = null) : ISelectCharacterPetEntryDomainService
{
    /// <summary>The equipment slot the equipped creature sits in.</summary>
    private const int EquippedCreatureSlot = 25;

    /// <summary>What an item core says it is when it is a creature.</summary>
    private const byte CreatureItemKind = 5;

    public async ValueTask<SelectCharacterPetRevival?> ReviveAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken)
    {
        var place = new OwnedCharacterId(session.AccountId, session.CharacterId);
        // Asked first, so a pairing that does not hold fails here rather than only if a write
        // happens to be staged further down.
        if (!await stores.ReadAsync(characters, rows => rows.HoldsAsync(place, cancellationToken))
                .ConfigureAwait(false))
            throw new InvalidDataException("Entry mutation character ownership does not match.");

        long creatureKey;
        int before;
        if (creatureDomains is { } domain)
        {
            var creatureStore = domain.GetStore(session.CharacterId);
            var current = await creatureStore.CurrentAsync(cancellationToken).ConfigureAwait(false);
            if (current is null || current.Stomach > 0) return null;
            creatureKey = current.ItemUid;
            before = current.Stomach;
            await creatureStore.ReviveIfEmptyAsync(current.ItemUid, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Compatibility for callers still constructing this service without the creature store.
            var equipment = ContainerId.ForCharacter(
                session.CharacterId, InventoryContainerType.Equipment);
            var equipped = await stores.ReadAsync(
                items, held => held.GetAsync(equipment, EquippedCreatureSlot, cancellationToken))
                .ConfigureAwait(false);
            if (equipped?.Snapshot is not { LegacyKind: CreatureItemKind, ItemUid: not null }) return null;
            if (equipped.ItemUid is not { } worn) return null;
            creatureKey = worn.Value;
            var creature = await stores.ReadAsync(
                characters, rows => rows.Creatures.ReadAsync(place, creatureKey, cancellationToken))
                .ConfigureAwait(false);
            if (creature is null || creature.Field04 > 0) return null;
            before = creature.Field04;
            await stores.ChangeAsync(characters, rows =>
            {
                rows.Creatures.Write(place, new StoredCreature(creatureKey, 1, OnlyWhenAtMostZero: true));
                return ValueTask.CompletedTask;
            }, cancellationToken).ConfigureAwait(false);
        }
        var mutation = new PetRevivalMutation(
            creatureKey, Math.Clamp(before, 0, byte.MaxValue), 1);
        var pets = result.CharacterEntry.Features.Pets
            .Select(value => value.PetKey == mutation.CreatureKey
                ? value with { Field04 = mutation.After } : value)
            .ToArray();
        if (pets.All(value => value.PetKey != mutation.CreatureKey))
            throw new InvalidDataException("Revived creature is absent from the prepared character state.");
        var updated = result with
        {
            CharacterEntry = result.CharacterEntry with
            {
                Features = result.CharacterEntry.Features with { Pets = pets }
            }
        };
        return new SelectCharacterPetRevival(updated,
            new PetRevivalState(
                session.WireUserId, checked((int)mutation.CreatureKey), mutation.After));
    }
}
