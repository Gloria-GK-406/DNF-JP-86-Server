using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterCurrentStateQueryDomainService(
    ICharacterDomainService characters,
    ICharacterInventoryEntryDomainService inventories,
    IAccountInventoryEntryDomainService accountInventories,
    ICharacterEntryStateDomainService entries,
    IAccountEntryStateDomainService accounts,
    IEquippedCreatureQuery equippedCreature,
    IWorldContentEntryRuleService worldContent,
    ICharacterEntryPreparationRuleService rules,
    ICharacterPvpSkillProfileRepository pvpSkills,
    IChannelEntryDomainService channels,
    ITownMovementDomainService towns,
    ISessionAdjacentEntryDomainService adjacent,
    IOnlineCharacterRuntimeDomainService onlineCharacters,
    TimeProvider timeProvider,
    IEventManagementDomainService? events = null,
    IWeeklyAttendanceDomainService? attendance = null,
    IJoustProjectionDomainService? joust = null,
    IEpicBookDomainService? epicBook = null,
    Dfo.Server.Abstractions.Achievements.ITitleBookDomainService? titles = null,
    Dfo.Server.Abstractions.Achievements.IAchievementDomainService? achievements = null, ICollectionBoxDomainService? collections = null,
    ISouthernDaleQuestDomainService? southern = null) : ICharacterCurrentStateQueryDomainService
{
    public async ValueTask<SelectCharacterResult> ReadAsync(
        SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        var position = onlineCharacters.PositionOf(session)
            ?? throw new InvalidDataException("The selected character has no live position.");
        var roster = await characters.GetAccountRosterAsync(session.AccountId, cancellationToken)
            .ConfigureAwait(false);
        if (roster.AccountId != session.AccountId)
            throw new InvalidDataException("The account roster belongs to another account.");
        var member = roster.Characters.SingleOrDefault(value => value.CharacterId == session.CharacterId)
            ?? throw new InvalidDataException("Selected character is absent from the account roster.");
        var character = await characters.LoadForSelectionAsync(
            session.AccountId, member.SlotIndex, cancellationToken).ConfigureAwait(false);
        if (character.Identity.CharacterId != session.CharacterId)
            throw new InvalidDataException("The selected character no longer occupies its roster slot.");
        character = character with { Position = position };
        var channel = await channels.ResolveAsync(session.ListenerPort, cancellationToken).ConfigureAwait(false);
        var characterInventory = await inventories.ReadCurrentAsync(
            session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        var accountInventory = await accountInventories.LoadAsync(
            session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        var characterEntry = await entries.LoadAsync(
            session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        var accountEntry = await accounts.LoadAsync(session.AccountId, cancellationToken).ConfigureAwait(false);
        var readAt = timeProvider.GetUtcNow();
        characterInventory = await SupportCreatureMarkerProjection.CompleteAsync(
            characterInventory, rules, readAt, cancellationToken).ConfigureAwait(false);
        accountEntry = await CharacterStateProjection.CompleteSupportCharacterProjectionAsync(
            session.AccountId, character, roster, accountEntry, characters, inventories.ReadCurrentAsync,
            entries, rules, readAt, cancellationToken).ConfigureAwait(false);

        characterEntry = CharacterStateProjection.CompleteAppearanceProjection(character, characterEntry, characterInventory);
        if (achievements is not null)
            characterEntry = characterEntry with { Features = characterEntry.Features with
            { Collections = characterEntry.Features.Collections with
            { Achievements = await achievements.GetAsync(session.CharacterId, cancellationToken).ConfigureAwait(false) } } };
        if (titles is not null)
            characterEntry = characterEntry with { Features = characterEntry.Features with
            { Collections = characterEntry.Features.Collections with
            { TitleBooks = await titles.GetAsync(session.CharacterId, cancellationToken).ConfigureAwait(false) } } };
        var contentSubject = CharacterStateProjection.BuildContentSubject(
            equippedCreature, character, characterEntry, characterInventory);
        if (southern is not null)
            contentSubject = contentSubject with { SouthernDaleQuestAvailable = await southern.CanAcceptAsync(session.CharacterId, cancellationToken).ConfigureAwait(false) };
        var content = await worldContent.LoadAsync(contentSubject, cancellationToken).ConfigureAwait(false);
        // Channel flags are independent of login placement. No arrival gate is needed to query
        // a character already standing in a declared town area, such as Time Gate (town 12).
        var channelPolicy = new ChannelEntryPolicyState(
            channel.ChannelId,
            new EntrySpawnState(position.TownId, position.AreaId, position.X, position.Y,
                position.Direction, 3, channel.IsTransient,
                await towns.SharesTownPresenceAsync(position.TownId, position.AreaId, cancellationToken)
                    .ConfigureAwait(false)),
            PersistsPosition: !channel.IsTransient,
            PvpSkillProfileApplies: channel.IsPvp);
        if (channel.IsPvp)
        {
            var profile = await pvpSkills.ReadAsync(
                session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
            characterEntry = characterEntry with
            {
                Skills = CharacterStateProjection.MergePvpProfile(characterEntry.Skills, profile),
            };
        }
        else
            characterEntry = await CharacterStateProjection.CompleteSkillProjectionAsync(
                character, characterEntry, rules, cancellationToken).ConfigureAwait(false);
        var adjacentState = await adjacent.LoadAsync(
            new SessionAdjacentSubject(session.CharacterId, session.AccountId, channel,
                position.TownId, position.AreaId), cancellationToken).ConfigureAwait(false);
        characterEntry = CharacterStateProjection.CompleteCollectionBoxProjection(characterEntry, content, readAt);
        var drawnRoster = await characters.GetAccountRosterWithAppearanceAsync(
            session.AccountId, cancellationToken).ConfigureAwait(false);
        var rosterEntry = await CharacterStateProjection.BuildRosterEntryAsync(
            session.AccountId, drawnRoster, session.CharacterId, characterEntry, entries, cancellationToken)
            .ConfigureAwait(false);

        return new SelectCharacterResult(
            character, characterEntry, characterInventory, accountInventory, accountEntry,
            new GameWorldEntryState(readAt, content, channelPolicy, contentSubject),
            new CharacterSessionEntryState(session.AccountId, session.CharacterId, adjacentState),
            roster, rosterEntry,
            events is null || attendance is null ? null : Dfo.Server.Application.Events.EventPlayerSnapshotProjection.WithScheduledDungeons(new EventPlayerSnapshot(
                await events.GetEventInfoListAsync(session.AccountId, readAt, cancellationToken),
                await attendance.StateAsync(session.AccountId, readAt, cancellationToken),
                joust is null ? null : await joust.ReadAsync(session.CharacterId, readAt, cancellationToken))),
            epicBook is null ? [] : await epicBook.GetBalancesAsync(session.AccountId, cancellationToken).ConfigureAwait(false))
        { CollectionBoxes = collections is null ? null : await collections.ReadAsync(session.CharacterId, cancellationToken).ConfigureAwait(false) };
    }
}
