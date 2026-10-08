using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Inventory;

namespace Dfo.Server.Application.Characters;

public sealed class SelectCharacterPreparationService(
    ICharacterDomainService characters,
    ICharacterInventoryEntryDomainService characterInventoryEntries,
    IAccountInventoryEntryDomainService accountInventoryEntries,
    ICharacterEntryStateDomainService characterEntries,
    IAccountEntryStateDomainService accountEntries,
    IEquippedCreatureQuery equippedCreature,
    IWorldContentEntryRuleService worldContent,
    ICharacterEntryPreparationRuleService preparationRules,
    ICharacterPvpSkillProfileRepository pvpSkills,
    IDungeonPersistentEffectRecoveryRepository dungeonEffects,
    IExpertSkillContractReconciliationRepository expertSkillReconciliation,
    IChannelEntryPolicy channels,
    IEntryPlacementRuleService placement,
    ISessionAdjacentEntryDomainService sessionAdjacent,
    ISelectedSessionPositionPersistence positions,
    TimeProvider timeProvider,
    Dfo.Server.Abstractions.Events.IEventManagementDomainService? events = null,
    Dfo.Server.Abstractions.Events.IWeeklyAttendanceDomainService? attendance = null,
    Dfo.Server.Abstractions.Events.IJoustProjectionDomainService? joust = null,
    IEpicBookDomainService? epicBook = null,
    Dfo.Server.Abstractions.Achievements.IAchievementDomainService? achievements = null,
    Dfo.Server.Abstractions.Achievements.ITitleBookDomainService? titles = null) : ISelectCharacterPreparationDomainService
{
    public async ValueTask<SelectCharacterResult> PlaceAtSpawnAsync(
        SelectedSessionIdentity session,
        SelectCharacterResult prepared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(prepared);
        var spawn = prepared.World.Channel.Spawn;
        var resolved = new CharacterPositionState(
            spawn.TownId, spawn.AreaId, spawn.X, spawn.Y, spawn.Direction);
        var placed = prepared with
        {
            Character = prepared.Character with
            {
                Position = resolved,
                LastInnTownId = prepared.World.Channel.PersistsPosition
                    ? spawn.TownId : prepared.Character.LastInnTownId,
            },
        };
        if (placed.World.Channel.PersistsPosition)
        {
            await positions.PersistAsync(
                session.AccountId, session.CharacterId, resolved, cancellationToken)
                .ConfigureAwait(false);
        }

        return placed;
    }

    public async ValueTask<ActiveCharacterState> PreflightAsync(
        AccountId accountId, ushort rosterIndex, ChannelEntryIdentity channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        // The wire index is a position in the roster the client was sent, not a slot.
        var character = await characters.LoadByRosterPositionAsync(
            accountId,
            rosterIndex,
            cancellationToken).ConfigureAwait(false);

        // Admission depends only on minimum character facts. Refuse before loading any
        // entry state so the rejected selection leaves the current online state untouched.
        if (channels.Refuse(channel, character.Progression.Level) is { } refusal)
            throw new ChannelAdmissionRefusedException(channel, refusal);

        return character;
    }

    public async ValueTask<SelectCharacterResult> PrepareAsync(
        ActiveCharacterState character, AccountId accountId, ChannelEntryIdentity channel,
        CancellationToken cancellationToken)
        => await BuildResultAsync(character, accountId, channel, cancellationToken).ConfigureAwait(false);

    private async ValueTask<SelectCharacterResult> BuildResultAsync(
        ActiveCharacterState character, AccountId accountId, ChannelEntryIdentity channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var recovery = await dungeonEffects.RecoverAsync(
            accountId, character.Identity.CharacterId, cancellationToken).ConfigureAwait(false);
        if (recovery.FailedCount > 0 || recovery.RemainingCount > 0)
            throw new InvalidDataException("Dungeon persistent effects were not completely recovered.");
        if (recovery.RequiresReload)
            character = await characters.LoadForSelectionAsync(
                accountId, character.Identity.SlotIndex, cancellationToken).ConfigureAwait(false);

        var roster = await characters.GetAccountRosterAsync(
            accountId,
            cancellationToken).ConfigureAwait(false);
        if (roster.AccountId != accountId
            || roster.Characters.All(value => value.CharacterId != character.Identity.CharacterId))
        {
            throw new InvalidDataException("Selected character is absent from the account roster.");
        }

        var characterInventory = await characterInventoryEntries.LoadAsync(accountId, character.Identity.CharacterId, cancellationToken)
            .ConfigureAwait(false);
        var accountInventory = await accountInventoryEntries.LoadAsync(
            accountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);
        if (achievements is not null)
        {
            await achievements.PrepareAsync(character.Identity.CharacterId, character.Progression.Level, cancellationToken).ConfigureAwait(false);
            await achievements.UnlockSatisfiedAchievementsAsync(character.Identity.CharacterId, cancellationToken).ConfigureAwait(false);
        }
        if (titles is not null)
            await titles.PrepareAsync(character.Identity.CharacterId, cancellationToken).ConfigureAwait(false);
        var characterEntry = await characterEntries.LoadAsync(
            accountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);
        var learnedSkillIds = characterEntry.Skills.Pages.SelectMany(value => value.Skills)
            .Select(value => checked((int)value.SkillId)).Distinct().ToArray();
        var reconciliationDefinitions = await preparationRules.SkillPointsAsync(
            character.Identity.Job, character.Identity.GrowType, character.Progression.Level,
            learnedSkillIds, cancellationToken).ConfigureAwait(false);
        var freeBaseline = reconciliationDefinitions.FreeGrants.GroupBy(value => value.SkillId)
            .ToDictionary(group => group.Key, group => group.Max(value => value.Level));
        if (await expertSkillReconciliation.ReconcileAsync(
                accountId, character.Identity.CharacterId,
                preparationRules.OverSkillPremiumTypes(), reconciliationDefinitions.Skills,
                freeBaseline, cancellationToken).ConfigureAwait(false))
            characterEntry = await characterEntries.LoadAsync(
                accountId, character.Identity.CharacterId, cancellationToken).ConfigureAwait(false);
        var accountEntry = await accountEntries.LoadAsync(
            accountId,
            cancellationToken).ConfigureAwait(false);
        var preparedAt = timeProvider.GetUtcNow();
        characterInventory = await SupportCreatureMarkerProjection.CompleteAsync(
            characterInventory, preparationRules, preparedAt, cancellationToken).ConfigureAwait(false);
        accountEntry = await CharacterStateProjection.CompleteSupportCharacterProjectionAsync(
            accountId, character, roster, accountEntry,
            characters, characterInventoryEntries.LoadAsync, characterEntries,
            preparationRules, preparedAt,
            cancellationToken).ConfigureAwait(false);

        characterEntry = CharacterStateProjection.CompleteTitleAppearanceProjection(characterEntry, characterInventory);
        var contentSubject = CharacterStateProjection.BuildContentSubject(equippedCreature, character, characterEntry, characterInventory);
        var content = await worldContent.LoadAsync(contentSubject, cancellationToken)
            .ConfigureAwait(false);
        var channelPolicy = await placement.ResolveAsync(
            channel,
            character.LastInnTownId != 0 ? character.LastInnTownId : character.Position.TownId,
            cancellationToken).ConfigureAwait(false);
        if (channelPolicy.PvpSkillProfileApplies)
        {
            var initial = await BuildInitialPvpSkillProfileAsync(
                character, characterEntry.Skills, preparationRules, cancellationToken).ConfigureAwait(false);
            var profile = await pvpSkills.LoadOrInitializeAsync(
                accountId, character.Identity.CharacterId, initial, cancellationToken).ConfigureAwait(false);
            characterEntry = characterEntry with
            {
                Skills = CharacterStateProjection.MergePvpProfile(characterEntry.Skills, profile),
            };
        }
        else
            characterEntry = await CharacterStateProjection.CompleteSkillProjectionAsync(
                character, characterEntry, preparationRules, cancellationToken)
                .ConfigureAwait(false);
        var adjacent = await sessionAdjacent.LoadAsync(
            new SessionAdjacentSubject(
                character.Identity.CharacterId,
                accountId,
                channel,
                channelPolicy.Spawn.TownId,
                channelPolicy.Spawn.AreaId),
            cancellationToken).ConfigureAwait(false);

        characterEntry = CharacterStateProjection.CompleteCollectionBoxProjection(characterEntry, content, preparedAt);

        // The roster read above answers whether the selected character is on the account, and that
        // question does not need anyone's clothes -- it stays on the listing that works nothing out,
        // which is one of the four call sites that listing was split off for. The frame below does
        // need them, so it takes the listing that has them. Two reads of the same summaries is the
        // price of not putting an equipment read behind a membership check.
        var drawnRoster = await characters.GetAccountRosterWithAppearanceAsync(
            accountId, cancellationToken).ConfigureAwait(false);
        var rosterEntry = await CharacterStateProjection.BuildRosterEntryAsync(
            accountId, drawnRoster, character.Identity.CharacterId, characterEntry,
            characterEntries, cancellationToken).ConfigureAwait(false);

        var result = new SelectCharacterResult(
            character,
            characterEntry,
            characterInventory,
            accountInventory,
            accountEntry,
            new GameWorldEntryState(preparedAt, content, channelPolicy, contentSubject),
            new CharacterSessionEntryState(
                accountId,
                character.Identity.CharacterId,
                adjacent),
            roster,
            rosterEntry,
            events is null || attendance is null ? null : new Dfo.Server.Data.Events.EventPlayerSnapshot(
                await events.GetEventInfoListAsync(accountId,preparedAt,cancellationToken),
                await attendance.StateAsync(accountId,preparedAt,cancellationToken),
                joust is null ? null : await joust.ReadAsync(character.Identity.CharacterId,preparedAt,cancellationToken)),
            epicBook is null ? [] : await epicBook.GetBalancesAsync(accountId, cancellationToken).ConfigureAwait(false));

        return result;
    }

    internal static async ValueTask<CharacterSkillsEntryState> BuildInitialPvpSkillProfileAsync(
        ActiveCharacterState character, CharacterSkillsEntryState basis,
        ICharacterEntryPreparationRuleService rules, CancellationToken cancellationToken)
    {
        var definitions = await rules.SkillPointsAsync(character.Identity.Job, character.Identity.GrowType,
            character.Progression.Level, [], cancellationToken).ConfigureAwait(false);
        var grants = definitions.FreeGrants.GroupBy(value => value.SkillId)
            .Select(group => group.OrderByDescending(value => value.Level).First()).ToArray();
        var occupied = new HashSet<int>();
        var skills = new List<CharacterSkillState>();
        foreach (var grant in grants.OrderBy(value => value.IsInitial ? 0 : 1))
        {
            definitions.Skills.TryGetValue(grant.SkillId, out var definition);
            var active = definition?.IsActive == true;
            var grantLevel = grant.Level;
            if (definition is not null)
                grantLevel = checked((byte)Math.Clamp(SkillPointLedgerRules.FreeLevel(
                    definition, grantLevel, character.Progression.Level), 0, byte.MaxValue));
            int slot;
            if (grant.IsInitial)
                slot = FirstFree(active ? 0 : 102, active ? 102 : 204);
            else
            {
                var group = ReformGroup(definition?.RawGroup ?? 0, active, definition?.NumberOfGrowTypes ?? 0);
                slot = active && character.Identity.Job != 9
                    ? FirstFree(0, 6) is var primary && primary >= 0 ? primary : FirstFree(198, 204)
                    : FirstFree(new[] { 6, 54, 102, 150 }[Math.Clamp(group, 0, 3)],
                        new[] { 54, 102, 150, 198 }[Math.Clamp(group, 0, 3)]);
            }
            if (slot < 0) slot = FirstFree(0, 204);
            if (slot < 0) throw new InvalidDataException("PvP initial skill profile has no free slot.");
            occupied.Add(slot);
            skills.Add(new CharacterSkillState(checked((short)slot), checked((ushort)grant.SkillId), grantLevel, []));
        }
        return new CharacterSkillsEntryState(
            [new CharacterSkillPageState(0, skills), new CharacterSkillPageState(1, skills)], [],
            basis.SkillPointBasis, basis.DarkKnightCombos, basis.KnightShieldDeck,
            new CharacterSkillProtocolMirrorState(
                ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue));

        int FirstFree(int start, int end)
        { for (var index = start; index < end; index++) if (!occupied.Contains(index)) return index; return -1; }
        static int ReformGroup(int raw, bool active, int grows) => active ? 3
            : raw is >= 0 and <= 3 ? grows <= 2 ? 1 : 0 : raw == 4 ? 2 : raw;
    }

}
