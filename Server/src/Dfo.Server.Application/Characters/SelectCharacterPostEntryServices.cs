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
    ICharacterQuestLogDomainService logs, IQuestQueryDomainService query) : ISelectCharacterQuestEntryDomainService
{
    public async ValueTask<SelectCharacterQuestSynchronization?> SynchronizeAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken)
    {
        var carried = result.CharacterEntry.Quests.ActiveQuests
            .Select(x => new CarriedQuest(x.Slot, x.QuestId, x.TriggerValue)).ToArray();
        var evaluated = await query.ReadAcceptedAsync(session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        var byQuestSlot = evaluated.ToDictionary(x => x.Quest.Slot, x => x.Quest);
        var planned = new List<QuestTriggerUpdate>();
        foreach (var active in carried)
        {
            if (!byQuestSlot.TryGetValue(active.Slot, out var effective) || effective.QuestId != active.QuestId)
                throw new InvalidDataException($"Quest {active.QuestId} changed during entry synchronization.");
            if (effective.TriggerValue != active.TriggerValue)
                planned.Add(new QuestTriggerUpdate(active.Slot, active.QuestId, active.TriggerValue, effective.TriggerValue));
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

}

/// <remarks>
/// The equipped item and its creature state settle in one database transaction.
/// </remarks>
internal sealed class SelectCharacterPetEntryService(
    ICharacterDomainStore characters,
    AmbientStoreAccess stores,
    ICreatureDomainService creatureDomains) : ISelectCharacterPetEntryDomainService
{
    public async ValueTask<SelectCharacterPetRevival?> ReviveAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken)
    {
        var place = new OwnedCharacterId(session.AccountId, session.CharacterId);
        // Asked first, so a pairing that does not hold fails here rather than only if a write
        // happens to be staged further down.
        if (!await stores.ReadAsync(characters, rows => rows.HoldsAsync(place, cancellationToken))
                .ConfigureAwait(false))
            throw new InvalidDataException("Entry mutation character ownership does not match.");

        var creatureStore = creatureDomains.GetStore(session.CharacterId);
        var current = await creatureStore.CurrentAsync(cancellationToken).ConfigureAwait(false);
        if (current is null || current.Stomach > 0) return null;
        var creatureKey = current.ItemUid;
        var before = current.Stomach;
        await creatureStore.ReviveIfEmptyAsync(current.ItemUid, cancellationToken).ConfigureAwait(false);
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
