using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterEntryService(
    ICharacterService characters,
    IInventoryEntryService inventory,
    ICharacterEntryStateService characterEntries,
    IAccountEntryStateService accountEntries,
    IWorldContentEntryService worldContent,
    IChannelEntryPolicy channels,
    IEntryPlacementService placement,
    ISessionAdjacentEntryService sessionAdjacent,
    IActiveCharacterService activeCharacters,
    TimeProvider timeProvider) : ICharacterEntryService
{
    /// <summary>
    /// Where an equipped creature sits in the equipment container.
    /// </summary>
    /// <remarks>Reverse-engineered from the legacy pet inventory layout.</remarks>
    private const int CreatureEquipSlot = 24;

    /// <summary>The item kind the legacy inventory uses for creatures.</summary>
    private const byte CreatureItemKind = 5;

    public async ValueTask<SelectCharacterResult> EnterAsync(
        AccountId accountId,
        ushort slotIndex,
        ChannelEntryIdentity channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var character = await characters.LoadForSelectionAsync(
            accountId,
            slotIndex,
            cancellationToken).ConfigureAwait(false);

        // Admission is decided as soon as the character is known and before any other
        // state is touched, so a refused entry does no loading and leaves nothing behind.
        if (channels.Refuse(channel, character.Progression.Level) is { } refusal)
            throw new ChannelAdmissionRefusedException(channel, refusal);

        var inventoryEntry = await inventory.LoadAsync(
            accountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);

        var characterEntry = await characterEntries.LoadAsync(
            accountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);

        var accountEntry = await accountEntries.LoadAsync(
            accountId,
            cancellationToken).ConfigureAwait(false);

        // Content is resolved against this character's own progress, so the result carries
        // answers rather than catalogs. It must be complete before publication: a consumer
        // that sees a published character is entitled to a complete entry result.
        var content = await worldContent.LoadAsync(
            BuildContentSubject(character, characterEntry, inventoryEntry.CharacterInventory),
            cancellationToken).ConfigureAwait(false);

        var channelPolicy = await placement.ResolveAsync(
            channel,
            character.Position.TownId,
            cancellationToken).ConfigureAwait(false);

        // Asked about the place the character is arriving at, not the one they left.
        var adjacent = await sessionAdjacent.LoadAsync(
            new SessionAdjacentSubject(
                character.Identity.CharacterId,
                accountId,
                channel,
                channelPolicy.Spawn.TownId,
                channelPolicy.Spawn.AreaId),
            cancellationToken).ConfigureAwait(false);

        activeCharacters.Publish(accountId, character);
        var preparedAt = timeProvider.GetUtcNow();
        return new SelectCharacterResult(
            character,
            characterEntry,
            inventoryEntry.CharacterInventory,
            inventoryEntry.AccountInventory,
            accountEntry,
            new GameWorldEntryState(preparedAt, content, channelPolicy),
            new CharacterSessionEntryState(accountId, character.Identity.CharacterId, adjacent));
    }

    private static WorldContentSubject BuildContentSubject(
        ActiveCharacterState character,
        CharacterEntryState entry,
        CharacterInventoryEntryState characterInventory)
    {
        var claimedGroups = new List<int>();
        foreach (var group in entry.Progression.DailyChallenges)
        {
            if (group.RewardClaimed)
                claimedGroups.Add(checked((int)group.GroupId));
        }

        var boxes = new List<CollectionBoxProgressSubject>(entry.Features.Collections.CollectionBoxes.Count);
        foreach (var box in entry.Features.Collections.CollectionBoxes)
        {
            var collected = new List<int>(box.Slots.Count);
            foreach (var slot in box.Slots)
                collected.Add(slot.ItemId);

            boxes.Add(new CollectionBoxProgressSubject(box.BoxIndex, collected));
        }

        return new WorldContentSubject(
            character.Progression.Level,
            character.Identity.Job,
            character.Identity.GrowType,
            entry.Quests.ClearedQuestIds,
            claimedGroups,
            boxes,
            ResolveEquippedCreatureId(characterInventory));
    }

    /// <summary>
    /// The creature the character has equipped, or zero when none is.
    /// </summary>
    /// <remarks>
    /// Zero is an ordinary outcome, not a failure: it is why the welcome packet is skipped.
    /// </remarks>
    private static int ResolveEquippedCreatureId(CharacterInventoryEntryState characterInventory)
    {
        foreach (var container in characterInventory.Containers)
        {
            if (container.ContainerType is not InventoryContainerType.Equipment)
                continue;

            foreach (var item in container.Items)
            {
                if (item.SlotIndex == CreatureEquipSlot && item.Core.ItemKind == CreatureItemKind)
                    return item.Core.ItemId;
            }
        }

        return 0;
    }
}
