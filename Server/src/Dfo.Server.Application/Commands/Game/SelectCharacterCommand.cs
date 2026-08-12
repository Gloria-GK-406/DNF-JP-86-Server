using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Commands.Game;

/// <summary>
/// Enter the game with one of an account's characters.
/// </summary>
/// <param name="Channel">
/// The channel the connection arrived on. It is resolved before dispatch because entry
/// policy — admission, spawn, position persistence and duelling applicability — all follow
/// from it, and a command that had to ask which listener it came from would be reaching
/// back into the transport.
/// </param>
public sealed record SelectCharacterCommand(
    AccountId AccountId,
    ushort SlotIndex,
    ChannelEntryIdentity Channel) : ICommand<SelectCharacterResult>, IAccountCommand;

public sealed class SelectCharacterCommandHandler(
    ICharacterService characters,
    ICharacterInventoryEntryService characterInventoryEntries,
    IAccountInventoryEntryService accountInventoryEntries,
    ICharacterEntryStateService characterEntries,
    IAccountEntryStateService accountEntries,
    IEquippedCreatureQuery equippedCreature,
    IWorldContentEntryService worldContent,
    IChannelEntryPolicy channels,
    IEntryPlacementService placement,
    ISessionAdjacentEntryService sessionAdjacent,
    IActiveCharacterService activeCharacters,
    TimeProvider timeProvider)
    : ICommandHandler<SelectCharacterCommand, SelectCharacterResult>
{
    public async ValueTask<SelectCharacterResult> HandleAsync(
        CommandExecutionContext context,
        SelectCharacterCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Channel);

        var character = await characters.LoadForSelectionAsync(
            command.AccountId,
            command.SlotIndex,
            cancellationToken).ConfigureAwait(false);

        // Admission depends only on minimum character facts. Refuse before loading any
        // entry state so the rejected selection leaves the current online state untouched.
        if (channels.Refuse(command.Channel, character.Progression.Level) is { } refusal)
            throw new ChannelAdmissionRefusedException(command.Channel, refusal);

        var characterInventory = await characterInventoryEntries.LoadAsync(
            command.AccountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);
        var accountInventory = await accountInventoryEntries.LoadAsync(
            command.AccountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);
        var characterEntry = await characterEntries.LoadAsync(
            command.AccountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);
        var accountEntry = await accountEntries.LoadAsync(
            command.AccountId,
            cancellationToken).ConfigureAwait(false);

        var content = await worldContent.LoadAsync(
            BuildContentSubject(character, characterEntry, characterInventory),
            cancellationToken).ConfigureAwait(false);
        var channelPolicy = await placement.ResolveAsync(
            command.Channel,
            character.Position.TownId,
            cancellationToken).ConfigureAwait(false);
        var adjacent = await sessionAdjacent.LoadAsync(
            new SessionAdjacentSubject(
                character.Identity.CharacterId,
                command.AccountId,
                command.Channel,
                channelPolicy.Spawn.TownId,
                channelPolicy.Spawn.AreaId),
            cancellationToken).ConfigureAwait(false);

        var result = new SelectCharacterResult(
            character,
            characterEntry,
            characterInventory,
            accountInventory,
            accountEntry,
            new GameWorldEntryState(timeProvider.GetUtcNow(), content, channelPolicy),
            new CharacterSessionEntryState(
                command.AccountId,
                character.Identity.CharacterId,
                adjacent));

        // Publication is the only state change and deliberately remains last. A consumer
        // that sees the new active character is entitled to the complete result above.
        activeCharacters.Publish(command.AccountId, character);
        return result;
    }

    private WorldContentSubject BuildContentSubject(
        ActiveCharacterState character,
        CharacterEntryState entry,
        CharacterInventoryEntryState characterInventory)
    {
        var claimedGroups = entry.Progression.DailyChallenges
            .Where(group => group.RewardClaimed)
            .Select(group => checked((int)group.GroupId))
            .ToArray();
        var boxes = entry.Features.Collections.CollectionBoxes
            .Select(box => new CollectionBoxProgressSubject(
                box.BoxIndex,
                box.Slots.Select(slot => slot.ItemId).ToArray()))
            .ToArray();
        var clearedQuestFlags = entry.Quests.VisibilityFlags
            .Select((value, questId) => (questId, value))
            .Where(pair => pair.value != 0)
            .ToDictionary(pair => pair.questId, pair => checked((int)pair.value));

        return new WorldContentSubject(
            character.Progression.Level,
            character.Identity.Job,
            character.Identity.GrowType,
            clearedQuestFlags,
            claimedGroups,
            boxes,
            equippedCreature.GetItemId(characterInventory),
            entry.Public.Subtype1.EquippedCreatureLevel);
    }
}
