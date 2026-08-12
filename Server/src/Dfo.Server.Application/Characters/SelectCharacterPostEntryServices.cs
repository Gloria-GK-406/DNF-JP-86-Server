using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
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
    SelectCharacterAcceptedQuestsProjection Projection);

public interface ISelectCharacterPetEntryService
{
    ValueTask<SelectCharacterPetRevival?> ReviveAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken);
}

public sealed record SelectCharacterPetRevival(
    SelectCharacterResult Result,
    SelectCharacterPetRevivalProjection Projection);

internal sealed class SelectCharacterQuestEntryService(
    IQuestDefinitionSource quests,
    ICharacterEntryMutationRepository mutations) : ISelectCharacterQuestEntryService
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
        var committed = await mutations.UpdateSeekingQuestTriggersAsync(
            session.AccountId, session.CharacterId, planned, cancellationToken).ConfigureAwait(false);
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
            new SelectCharacterAcceptedQuestsProjection(bySlot.Values.OrderBy(value => value.Slot)
            .Select(value => new SelectCharacterAcceptedQuestProjection(
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

internal sealed class SelectCharacterPetEntryService(ICharacterEntryMutationRepository mutations)
    : ISelectCharacterPetEntryService
{
    public async ValueTask<SelectCharacterPetRevival?> ReviveAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken)
    {
        var mutation = await mutations.ReviveEquippedCreatureAsync(
            session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        if (mutation is null) return null;
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
            new SelectCharacterPetRevivalProjection(
                session.WireUserId, checked((int)mutation.CreatureKey), mutation.After));
    }
}
