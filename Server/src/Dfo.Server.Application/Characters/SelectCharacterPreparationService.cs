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
using System.Runtime.ExceptionServices;

namespace Dfo.Server.Application.Characters;

public interface ISelectCharacterPreparationService
{
    ValueTask<ActiveCharacterState> PreflightAsync(
        AccountId accountId, ushort rosterIndex, ChannelEntryIdentity channel,
        CancellationToken cancellationToken);
    ValueTask<SelectCharacterResult> PrepareAsync(
        ActiveCharacterState selected,
        AccountId accountId,
        ChannelEntryIdentity channel,
        CancellationToken cancellationToken);
}

public sealed class SelectCharacterPreparationService(
    ICharacterDomainService characters,
    ICharacterInventoryEntryDomainService characterInventoryEntries,
    IAccountInventoryEntryDomainService accountInventoryEntries,
    ICharacterEntryStateDomainService characterEntries,
    IAccountEntryStateDomainService accountEntries,
    IEquippedCreatureQuery equippedCreature,
    IWorldContentEntryRuleService worldContent,
    ISkillPointDefinitionSource skillPoints,
    ICharacterPvpSkillProfileRepository pvpSkills,
    IDungeonPersistentEffectRecoveryRepository dungeonEffects,
    IExpertSkillContractDefinitionSource expertContracts,
    IExpertSkillContractReconciliationRepository expertSkillReconciliation,
    ICreatureEquipmentLifetimeSource creatureLifetimes,
    IChannelEntryPolicy channels,
    IEntryPlacementRuleService placement,
    ISessionAdjacentEntryDomainService sessionAdjacent,
    TimeProvider timeProvider) : ISelectCharacterPreparationService
{
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

        var characterInventory = await characterInventoryEntries.LoadAsync(
            accountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);
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
        var reconciliationDefinitions = await skillPoints.GetAsync(
            character.Identity.Job, character.Identity.GrowType, character.Progression.Level,
            learnedSkillIds, cancellationToken).ConfigureAwait(false);
        var freeBaseline = reconciliationDefinitions.FreeGrants.GroupBy(value => value.SkillId)
            .ToDictionary(group => group.Key, group => group.Max(value => value.Level));
        if (await expertSkillReconciliation.ReconcileAsync(
                accountId, character.Identity.CharacterId,
                expertContracts.GetOverSkillPremiumTypes(), reconciliationDefinitions.Skills,
                freeBaseline, cancellationToken).ConfigureAwait(false))
            characterEntry = await characterEntries.LoadAsync(
                accountId, character.Identity.CharacterId, cancellationToken).ConfigureAwait(false);
        var accountEntry = await accountEntries.LoadAsync(
            accountId,
            cancellationToken).ConfigureAwait(false);
        var preparedAt = timeProvider.GetUtcNow();
        accountEntry = await CompleteSupportCharacterProjectionAsync(
            accountId, character, roster, accountEntry,
            characters, characterInventoryEntries, characterEntries,
            creatureLifetimes, preparedAt,
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
            var initial = await BuildInitialPvpSkillProfileAsync(
                character, characterEntry.Skills, skillPoints, cancellationToken).ConfigureAwait(false);
            characterEntry = characterEntry with
            {
                Skills = MergePvpProfile(characterEntry.Skills,
                    await pvpSkills.LoadOrInitializeAsync(
                        accountId, character.Identity.CharacterId, initial, cancellationToken).ConfigureAwait(false)),
            };
        }
        else
            characterEntry = await CompleteSkillProjectionAsync(
                character, characterEntry, skillPoints, cancellationToken).ConfigureAwait(false);
        var adjacent = await sessionAdjacent.LoadAsync(
            new SessionAdjacentSubject(
                character.Identity.CharacterId,
                accountId,
                channel,
                channelPolicy.Spawn.TownId,
                channelPolicy.Spawn.AreaId),
            cancellationToken).ConfigureAwait(false);

        characterEntry = CompleteCollectionBoxProjection(characterEntry, content, preparedAt);
        var rosterEntry = await BuildRosterEntryAsync(
            accountId, roster, character.Identity.CharacterId, characterEntry,
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
                DecodeAppearances(entry.Public.Appearance.EncodedEntries),
                summary.CloneTitleItemId));
        }
        return new CharacterRosterEntryState(
            roster.SlotLimit, roster.SlotLimit, checked((byte)selectedEntry.Public.Extras.ManageLevel),
            0, 0, 0, characters);
    }

    internal static IReadOnlyList<CharacterRosterAppearanceState> DecodeAppearances(byte[] blob)
    {
        if (blob is null || blob.Length == 0) return [];
        using var stream = new MemoryStream(blob, writable: false);
        using var reader = new BinaryReader(stream);
        var count = reader.ReadByte();
        var appearances = new List<CharacterRosterAppearanceState>(count);
        for (var index = 0; index < count; index++)
        {
            if (stream.Length - stream.Position < 23)
                throw new InvalidDataException("Character appearance blob is truncated.");
            appearances.Add(new CharacterRosterAppearanceState(
                reader.ReadByte(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadBytes(4),
                reader.ReadByte(), reader.ReadInt32(), reader.ReadUInt32(), reader.ReadByte()));
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Character appearance blob contains trailing bytes.");
        return appearances;
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
        ISkillPointDefinitionSource source,
        CancellationToken cancellationToken)
    {
        var learnedIds = entry.Skills.Pages.SelectMany(value => value.Skills)
            .Select(value => checked((int)value.SkillId)).Distinct().ToArray();
        var definitions = await source.GetAsync(
            character.Identity.Job,
            character.Identity.GrowType,
            character.Progression.Level,
            learnedIds,
            cancellationToken).ConfigureAwait(false);
        var baseline = definitions.FreeGrants.GroupBy(value => value.SkillId)
            .ToDictionary(group => group.Key, group => group.Max(value => value.Level));

        ushort RemainingFor(byte pageIndex, bool tp)
        {
            var spent = 0;
            var page = entry.Skills.Pages.SingleOrDefault(value => value.PageIndex == pageIndex);
            foreach (var learned in page?.Skills ?? [])
            {
                if (!definitions.Skills.TryGetValue(checked((int)learned.SkillId), out var skill))
                    continue; // Legacy treats an absent PVF skill definition as unchargeable.
                baseline.TryGetValue(skill.SkillId, out var freeLevel);
                if (skill.IsFixedLevel && character.Progression.Level >= skill.RequiredLevel)
                {
                    var fixedLevel = skill.FixedLevelBase
                        + (character.Progression.Level - skill.RequiredLevel) / Math.Max(1, skill.FixedLevelInterval)
                        * skill.FixedLevelAddPerInterval;
                    freeLevel = checked((byte)Math.Min(byte.MaxValue,
                        Math.Max(freeLevel, Math.Min(fixedLevel, skill.MaximumLevel))));
                }
                if (skill.UsesTp != tp || learned.Level <= freeLevel || skill.Costs.Count == 0)
                    continue;
                for (var level = freeLevel; level < learned.Level; level++)
                    spent = checked(spent + skill.Costs[Math.Min(level, skill.Costs.Count - 1)]);
            }
            var total = (tp ? definitions.TotalTp : definitions.TotalSp)
                + (tp ? entry.Skills.SkillPointBasis.BonusTp : entry.Skills.SkillPointBasis.BonusSp);
            return checked((ushort)Math.Clamp(total - spent, 0, ushort.MaxValue));
        }

        var mirror = new CharacterSkillProtocolMirrorState(
            RemainingFor(0, false), RemainingFor(1, false),
            RemainingFor(0, true), RemainingFor(1, true));
        return entry with { Skills = entry.Skills with { ProtocolMirror = mirror } };
    }

    internal static async ValueTask<CharacterSkillsEntryState> BuildInitialPvpSkillProfileAsync(
        ActiveCharacterState character, CharacterSkillsEntryState basis,
        ISkillPointDefinitionSource source, CancellationToken cancellationToken)
    {
        var definitions = await source.GetAsync(character.Identity.Job, character.Identity.GrowType,
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
            if (definition?.IsFixedLevel == true && character.Progression.Level >= definition.RequiredLevel)
            {
                var fixedLevel = definition.FixedLevelBase
                    + (character.Progression.Level - definition.RequiredLevel)
                    / Math.Max(1, definition.FixedLevelInterval) * definition.FixedLevelAddPerInterval;
                grantLevel = checked((byte)Math.Clamp(
                    Math.Max(grantLevel, Math.Min(fixedLevel, definition.MaximumLevel)), 0, byte.MaxValue));
            }
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
        ICreatureEquipmentLifetimeSource creatureLifetimes,
        DateTimeOffset preparedAt,
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
        var supportInventory = await inventories.LoadAsync(
            accountId, relation.SupportCharacterId, cancellationToken).ConfigureAwait(false);
        supportInventory = await CompleteSupportCreatureMarkerAsync(
            supportInventory, creatureLifetimes, preparedAt, cancellationToken)
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

    private static async ValueTask<CharacterInventoryEntryState> CompleteSupportCreatureMarkerAsync(
        CharacterInventoryEntryState inventory,
        ICreatureEquipmentLifetimeSource lifetimes,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken)
    {
        var containers = new List<InventoryContainerState>(inventory.Containers.Count);
        foreach (var container in inventory.Containers)
        {
            if (container.ContainerType != InventoryContainerType.Equipment)
            {
                containers.Add(container);
                continue;
            }

            var items = new List<InventoryItemState>(container.Items.Count);
            foreach (var item in container.Items)
            {
                if (item.SlotIndex != 24 || item.Core.ItemKind != 5)
                {
                    items.Add(item);
                    continue;
                }

                var lifetime = await lifetimes.GetAsync(
                    item.Core.ItemId, preparedAt, cancellationToken).ConfigureAwait(false);
                var marker = SupportCreatureMarkerProjection.Resolve(
                    lifetime, item.Core.ExpireTime, item.Core.Marker16, preparedAt);
                items.Add(item with { Noti2CreatureMarker = marker });
            }
            containers.Add(container with { Items = items });
        }
        return inventory with { Containers = containers };
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
