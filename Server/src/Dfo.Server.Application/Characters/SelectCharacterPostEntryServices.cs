using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

public interface ISelectCharacterQuestEntryService
{
    ValueTask<SelectCharacterQuestSynchronization?> SynchronizeAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken);
}

public sealed record SelectCharacterQuestSynchronization(
    SelectCharacterResult Result,
    AcceptedQuestSynchronizationState State);

public interface ISelectCharacterPetEntryService
{
    ValueTask<SelectCharacterPetRevival?> ReviveAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken);
}

public sealed record SelectCharacterPetRevival(
    SelectCharacterResult Result,
    PetRevivalState State);

internal sealed class SelectCharacterQuestEntryService(
    IQuestDefinitionSource quests,
    ICharacterQuestLogDomainService logs) : ISelectCharacterQuestEntryService
{
    public async ValueTask<SelectCharacterQuestSynchronization?> SynchronizeAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken)
    {
        var planned = new List<SeekingQuestTriggerUpdate>();
        foreach (var active in result.CharacterEntry.Quests.ActiveQuests.OrderBy(value => value.Slot))
        {
            var definition = await quests.GetAsync(active.QuestId, cancellationToken).ConfigureAwait(false);
            if (definition?.SeekingItems is not { Count: > 0 } requirements) continue;
            long missing = 0;
            foreach (var requirement in requirements)
                missing += Math.Max(0, requirement.RequiredCount - Count(result.CharacterInventory, requirement.ItemId));
            var after = ReplaceChannel0(active.TriggerValue, missing);
            if (after != active.TriggerValue)
                planned.Add(new SeekingQuestTriggerUpdate(active.Slot, active.QuestId, active.TriggerValue, after));
        }
        if (planned.Count == 0) return null;
        // Each write carries the value this entry snapshot was built from, so a slot that has
        // moved since fails rather than being overwritten with a decision made about the old one.
        var log = logs.LogOf(session.AccountId, session.CharacterId);
        var committed = new List<SeekingQuestTriggerUpdate>();
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
                    $"Seeking quest {update.QuestId} changed concurrently.");
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

    private static int Count(CharacterInventoryEntryState inventory, int itemId)
    {
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
/// Reviving reaches two kinds of state: which creature is equipped, which is an item, and what
/// condition that creature is in, which is the character's. Both are kept in the same database and
/// settle together, so a revival that cannot be written leaves nothing half done.
/// </remarks>
internal sealed class SelectCharacterPetEntryService(
    ICharacterStore characters,
    IItemStore items,
    AmbientStoreAccess stores) : ISelectCharacterPetEntryService
{
    /// <summary>The equipment slot the equipped creature sits in.</summary>
    private const int EquippedCreatureSlot = 24;

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

        var equipment = ContainerId.ForCharacter(
            session.CharacterId, InventoryContainerType.Equipment);
        var equipped = await stores.ReadAsync(
            items, held => held.ReadAsync(equipment, EquippedCreatureSlot, cancellationToken))
            .ConfigureAwait(false);
        if (equipped?.Core is not { ItemKind: CreatureItemKind } core || core.Value <= 0) return null;

        var creatureKey = (long)core.Value;
        var creature = await stores.ReadAsync(
            characters, rows => rows.Creatures.ReadAsync(place, creatureKey, cancellationToken))
            .ConfigureAwait(false);
        if (creature is null || creature.Field04 > 0) return null;
        await stores.ChangeAsync(characters, rows =>
        {
            rows.Creatures.Write(place, new StoredCreature(creatureKey, 1, OnlyWhenAtMostZero: true));
            return ValueTask.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);
        var mutation = new PetRevivalMutation(
            creatureKey, Math.Clamp(creature.Field04, 0, byte.MaxValue), 1);
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
