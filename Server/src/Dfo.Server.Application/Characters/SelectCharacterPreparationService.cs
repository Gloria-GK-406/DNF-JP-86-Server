using System.Runtime.ExceptionServices;
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
using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.OnlineUsers;

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
    ICharacterPositionDomainService? currentPositions = null) : ISelectCharacterPreparationDomainService
{
    public async ValueTask<SelectCharacterResult> LoadCurrentAsync(
        SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        var roster = await characters.GetAccountRosterAsync(session.AccountId, cancellationToken)
            .ConfigureAwait(false);
        var member = roster.Characters.SingleOrDefault(value => value.CharacterId == session.CharacterId)
            ?? throw new InvalidDataException("Selected character is absent from the account roster.");
        var character = await characters.LoadForSelectionAsync(
            session.AccountId, member.SlotIndex, cancellationToken).ConfigureAwait(false);
        var currentPosition = currentPositions?.Find(session.CharacterId);
        if (currentPosition is not null)
            character = character with { Position = currentPosition };
        var channel = await channels.ResolveAsync(session.ListenerPort, cancellationToken)
            .ConfigureAwait(false);
        var result = await BuildResultAsync(
            character, session.AccountId, channel, cancellationToken, selectionEntry: false)
            .ConfigureAwait(false);
        if (currentPosition is { } position)
            result = result with
            {
                World = result.World with
                {
                    Channel = result.World.Channel with
                    {
                        Spawn = result.World.Channel.Spawn with
                        {
                            TownId = position.TownId, AreaId = position.AreaId,
                            X = position.X, Y = position.Y, Direction = position.Direction,
                        },
                    },
                },
            };
        return result;
    }

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
            Character = prepared.Character with { Position = resolved },
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
        => await BuildResultAsync(character, accountId, channel, cancellationToken,
            selectionEntry: true).ConfigureAwait(false);

    private async ValueTask<SelectCharacterResult> BuildResultAsync(
        ActiveCharacterState character, AccountId accountId, ChannelEntryIdentity channel,
        CancellationToken cancellationToken, bool selectionEntry)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (selectionEntry)
        {
            var recovery = await dungeonEffects.RecoverAsync(
                accountId, character.Identity.CharacterId, cancellationToken).ConfigureAwait(false);
            if (recovery.FailedCount > 0 || recovery.RemainingCount > 0)
                throw new InvalidDataException("Dungeon persistent effects were not completely recovered.");
            if (recovery.RequiresReload)
                character = await characters.LoadForSelectionAsync(
                    accountId, character.Identity.SlotIndex, cancellationToken).ConfigureAwait(false);
        }

        var roster = await characters.GetAccountRosterAsync(
            accountId,
            cancellationToken).ConfigureAwait(false);
        if (roster.AccountId != accountId
            || roster.Characters.All(value => value.CharacterId != character.Identity.CharacterId))
        {
            throw new InvalidDataException("Selected character is absent from the account roster.");
        }

        var characterInventory = await (selectionEntry
            ? characterInventoryEntries.LoadAsync(accountId, character.Identity.CharacterId, cancellationToken)
            : characterInventoryEntries.ReadCurrentAsync(accountId, character.Identity.CharacterId, cancellationToken))
            .ConfigureAwait(false);
        var accountInventory = await accountInventoryEntries.LoadAsync(
            accountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);
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
        if (selectionEntry && await expertSkillReconciliation.ReconcileAsync(
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
        accountEntry = await CompleteSupportCharacterProjectionAsync(
            accountId, character, roster, accountEntry,
            characters, characterInventoryEntries, characterEntries,
            preparationRules, preparedAt, selectionEntry,
            cancellationToken).ConfigureAwait(false);

        var contentSubject = BuildContentSubject(character, characterEntry, characterInventory);
        var content = await worldContent.LoadAsync(contentSubject, cancellationToken)
            .ConfigureAwait(false);
        var channelPolicy = await placement.ResolveAsync(
            channel,
            character.Position.TownId,
            cancellationToken).ConfigureAwait(false);
        if (channelPolicy.PvpSkillProfileApplies)
        {
            var initial = selectionEntry
                ? await BuildInitialPvpSkillProfileAsync(
                    character, characterEntry.Skills, preparationRules, cancellationToken)
                    .ConfigureAwait(false)
                : null;
            var profile = selectionEntry
                ? await pvpSkills.LoadOrInitializeAsync(
                    accountId, character.Identity.CharacterId, initial!, cancellationToken)
                    .ConfigureAwait(false)
                : await pvpSkills.ReadAsync(
                    accountId, character.Identity.CharacterId, cancellationToken)
                    .ConfigureAwait(false);
            characterEntry = characterEntry with
            {
                Skills = MergePvpProfile(characterEntry.Skills, profile),
            };
        }
        else
            characterEntry = await CompleteSkillProjectionAsync(
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

        characterEntry = CompleteCollectionBoxProjection(characterEntry, content, preparedAt);

        // The roster read above answers whether the selected character is on the account, and that
        // question does not need anyone's clothes -- it stays on the listing that works nothing out,
        // which is one of the four call sites that listing was split off for. The frame below does
        // need them, so it takes the listing that has them. Two reads of the same summaries is the
        // price of not putting an equipment read behind a membership check.
        var drawnRoster = await characters.GetAccountRosterWithAppearanceAsync(
            accountId, cancellationToken).ConfigureAwait(false);
        var rosterEntry = await BuildRosterEntryAsync(
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
            rosterEntry);

        return result;
    }

    private static CharacterSkillsEntryState MergePvpProfile(
        CharacterSkillsEntryState normal, CharacterSkillsEntryState pvp) => normal with
    {
        Pages = pvp.Pages,
        ProtocolMirror = pvp.ProtocolMirror
            ?? throw new InvalidDataException("PvP skill protocol mirror is unavailable."),
    };

    /// <summary>The roster frame in the entry burst, drawn from what each character wears.</summary>
    /// <remarks>
    /// <b>It used to decode <c>characters.appearance_blob</c>, and that column is written once when
    /// a character is created and by nothing afterwards.</b> A player who dressed a character was
    /// therefore drawn the character they made rather than the one they have -- on this frame only,
    /// because the character-selection frame had already been moved onto what is worn. The two
    /// disagreed in the open until this.
    /// <para>
    /// The appearance now arrives on the summaries, worked out by the same listing the
    /// character-selection frame asks, which is what stops the two drifting apart again: there is
    /// one place that pairs a roster with what its characters wear, and both frames call it.
    /// </para>
    /// <para>
    /// A null appearance is refused rather than drawn as nothing. Null means "this listing did not
    /// work it out", which is true of the cheap listing and must never be true here; drawing it as
    /// an empty appearance would undress every character on the screen and look like a content bug
    /// rather than a wiring one.
    /// </para>
    /// </remarks>
    private static async ValueTask<CharacterRosterEntryState> BuildRosterEntryAsync(
        AccountId accountId,
        AccountCharacterRoster roster,
        CharacterId selectedId,
        CharacterEntryState selectedEntry,
        ICharacterEntryStateDomainService entries,
        CancellationToken cancellationToken)
    {
        var characters = new List<CharacterRosterMemberState>(roster.Characters.Count);
        foreach (var summary in roster.Characters.OrderBy(value => value.SlotIndex))
        {
            var entry = summary.CharacterId == selectedId
                ? selectedEntry
                : await entries.LoadAsync(accountId, summary.CharacterId, cancellationToken).ConfigureAwait(false);
            characters.Add(new CharacterRosterMemberState(
                summary.CharacterId, summary.SlotIndex, summary.Name, summary.Job, summary.GrowType,
                summary.Level,
                checked((uint)Math.Max(0, entry.Public.DerivedStats.Progress1)),
                checked((uint)Math.Max(0, entry.Public.DerivedStats.Progress2)),
                0,
                summary.Appearances ?? throw new InvalidDataException(
                    "The account roster this frame is drawn from did not work out appearances."),
                summary.CloneTitleItemId));
        }
        return new CharacterRosterEntryState(
            roster.SlotLimit, roster.SlotLimit, checked((byte)selectedEntry.Public.Extras.ManageLevel),
            0, 0, 0, characters);
    }

    private static CharacterEntryState CompleteCollectionBoxProjection(
        CharacterEntryState entry,
        WorldContentEntryState content,
        DateTimeOffset preparedAt)
    {
        // Content decides which boxes exist; character storage only supplies progress within them.
        // This used to run the other way round -- the entry state invented a fixed pair of box
        // indices and this projection demanded a definition for each -- which made entry fail
        // outright on any archive that did not happen to declare those two. The archive in use
        // declares boxes 1 and 2 while the hard-coded pair was 3 and 4, so it never could have
        // resolved. The reference server derives the set the same way, from its collect-box table.
        var stored = entry.Features.Collections.CollectionBoxes.ToDictionary(value => value.BoxIndex);
        var boxes = content.CollectionBoxes.OrderBy(value => value.BoxIndex).Select(definition =>
        {
            var index = checked((byte)definition.BoxIndex);
            var box = stored.TryGetValue(index, out var progress)
                ? progress
                : new CharacterCollectionBoxProgressState(index, []);

            uint remainingSeconds;
            byte statusFlags;
            if (definition.MaximumExpiresAt is not { } maximum)
            {
                remainingSeconds = 0;
                statusFlags = 1;
            }
            else if (maximum <= preparedAt)
            {
                remainingSeconds = uint.MaxValue;
                statusFlags = 0;
            }
            else
            {
                remainingSeconds = checked((uint)Math.Min(
                    uint.MaxValue,
                    Math.Floor((maximum - preparedAt).TotalSeconds)));
                statusFlags = 0;
            }

            return box with
            {
                Protocol = new CharacterCollectionBoxProtocolState(remainingSeconds, statusFlags),
            };
        }).ToArray();

        return entry with
        {
            Features = entry.Features with
            {
                Collections = entry.Features.Collections with { CollectionBoxes = boxes },
            },
        };
    }

    private static async ValueTask<CharacterEntryState> CompleteSkillProjectionAsync(
        ActiveCharacterState character,
        CharacterEntryState entry,
        ICharacterEntryPreparationRuleService rules,
        CancellationToken cancellationToken)
    {
        var learnedIds = entry.Skills.Pages.SelectMany(value => value.Skills)
            .Select(value => checked((int)value.SkillId)).Distinct().ToArray();
        var definitions = await rules.SkillPointsAsync(
            character.Identity.Job,
            character.Identity.GrowType,
            character.Progression.Level,
            learnedIds,
            cancellationToken).ConfigureAwait(false);
        // The arithmetic itself belongs to the rule service, which already holds the archive's
        // answers. Entry supplies what it has in hand; a character asked about its points later
        // supplies the same facts read from storage, and both get the one implementation.
        var mirror = rules.RemainingPoints(
            definitions,
            character.Progression.Level,
            entry.Skills.Pages,
            entry.Skills.SkillPointBasis);
        return entry with { Skills = entry.Skills with { ProtocolMirror = mirror } };
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

    private static async ValueTask<AccountEntryState> CompleteSupportCharacterProjectionAsync(
        AccountId accountId,
        ActiveCharacterState selected,
        AccountCharacterRoster roster,
        AccountEntryState account,
        ICharacterDomainService characters,
        ICharacterInventoryEntryDomainService inventories,
        ICharacterEntryStateDomainService entries,
        ICharacterEntryPreparationRuleService preparationRules,
        DateTimeOffset preparedAt,
        bool selectionEntry,
        CancellationToken cancellationToken)
    {
        var ownerRelations = account.Relations.CharacterRelations
            .Where(value => value.OwnerCharacterId == selected.Identity.CharacterId).ToArray();
        if (ownerRelations.Length == 0)
            return account;
        if (ownerRelations.Length != 1 || ownerRelations[0].Slot != 0)
            throw new InvalidDataException("Select-character supports exactly one owner relation in slot zero.");
        var relation = ownerRelations[0];
        var summary = roster.Characters.SingleOrDefault(
            value => value.CharacterId == relation.SupportCharacterId)
            ?? throw new InvalidDataException("Support character is absent from the account roster.");
        var support = await characters.LoadForSelectionAsync(
            accountId, summary.SlotIndex, cancellationToken).ConfigureAwait(false);
        var supportEntry = await entries.LoadAsync(
            accountId, relation.SupportCharacterId, cancellationToken).ConfigureAwait(false);
        var supportInventory = await (selectionEntry
            ? inventories.LoadAsync(accountId, relation.SupportCharacterId, cancellationToken)
            : inventories.ReadCurrentAsync(accountId, relation.SupportCharacterId, cancellationToken))
            .ConfigureAwait(false);
        supportInventory = await SupportCreatureMarkerProjection.CompleteAsync(
            supportInventory, preparationRules, preparedAt, cancellationToken)
            .ConfigureAwait(false);
        var completed = relation with
        {
            SupportProjection = new AccountSupportCharacterEntryState(
                support, supportEntry, supportInventory),
        };
        return account with
        {
            Relations = account.Relations with
            {
                CharacterRelations = account.Relations.CharacterRelations
                    .Select(value => value == relation ? completed : value).ToArray(),
            },
        };
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
            [.. entry.Quests.ActiveQuests.Select(quest => quest.QuestId)],
            claimedGroups,
            boxes,
            equippedCreature.GetItemId(characterInventory),
            entry.Public.DerivedStats.EquippedCreatureLevel);
    }
}
