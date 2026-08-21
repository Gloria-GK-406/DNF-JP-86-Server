using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// Everything a character is entered with, put together from the tables that hold it.
/// </summary>
/// <remarks>
/// Thirty-three tables become one record. None of them knows any of what makes that record a
/// character: that four rows must all be there or the character is not enterable at all, that a
/// combo page belongs to one job and a shield deck to another, that a daily-challenge claim
/// naming no group is a broken record rather than an ignorable one, that a schedule is three
/// slots or nothing, that the inbox is the twenty oldest live messages shown newest first. Each
/// of those is a decision about characters, so all of them are here.
///
/// The refusals are here too, for the same reason. A table can read a row whose value is out of
/// range perfectly well; whether that row may be used is not a question about rows.
///
/// One class in one file, deliberately. The old arrangement had this same assembly inside the
/// SQL, where it could not be read without reading the statements around it; splitting it across
/// several classes now would only move that problem rather than remove it.
/// </remarks>
internal sealed class CharacterEntryAssembly(
    ICharacterEntryTable characters,
    ICharacterSubtype0EntryTable subtype0,
    ICharacterSubtype1EntryTable subtype1,
    ICharacterInitFlagEntryTable initFlags,
    ICharacterHotkeyTable hotkeys,
    ICharacterDimensionTable dimensions,
    ICharacterDimensionFlagTable dimensionFlags,
    ICharacterSkillTable skills,
    ICharacterComboPageTable comboPages,
    ICharacterShieldDeckTable shieldDeck,
    ICharacterActiveQuestTable activeQuests,
    ICharacterQuestNotifyTable questNotifications,
    ICharacterQuestClearFlagTable questClearFlags,
    ICharacterGrowthWeaponTable growthWeapons,
    ICharacterCrystalContractTable crystalContract,
    ICharacterGoldLimitTable goldLimitRows,
    ICharacterDailyChallengeGroupTable challengeGroups,
    ICharacterDailyChallengeClaimTable challengeClaims,
    ICharacterDailyChallengeEntryTable challengeEntries,
    ICharacterDailyChallengeTailTable challengeTails,
    ICharacterDailyScheduleTable dailySchedules,
    ICharacterDungeonPermissionTable dungeonPermissions,
    ICharacterDailyCounterValueTable dailyCounters,
    ICharacterCreatureTable creatures,
    ICharacterTitleBookTable titleBooks,
    ICharacterAchievementTable achievements,
    ICharacterCollectionBoxTable collectionBoxes,
    ICharacterExpertJobTable expertJob,
    ICharacterExpertRecipeTable expertRecipes,
    ICharacterPvpMissionTable pvpMissions,
    IMailboxRecipientTable mailboxRecipients,
    IMailboxMessageTable mailboxMessages,
    IMailboxAttachmentTable mailboxAttachments,
    IGoldLimitRuleService goldLimits) : ICharacterEntryStateRepository
{
    /// <summary>How many of the inbox a character is shown on entry.</summary>
    private const int InboxPageSize = 20;

    /// <summary>The counter that says today's revive coin has been taken.</summary>
    private const string ReviveCoinCounter = "revive_coin_daily_claim";

    /// <summary>The counter that says today's doubled lottery reward has been taken.</summary>
    private const string LotteryDoubleCounter = "lottery_double_reward_used";

    public async ValueTask<CharacterEntryStorageRecord> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        if (accountId.Value <= 0 || characterId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accountId), "Identifiers must be positive.");
        }

        var profile = await LoadProfileAsync(accountId, characterId, cancellationToken)
            .ConfigureAwait(false);
        var skillState = await LoadSkillsAsync(characterId, profile.Job, cancellationToken)
            .ConfigureAwait(false);
        var quests = await LoadQuestsAsync(characterId, cancellationToken).ConfigureAwait(false);
        var progression = await LoadProgressionAsync(characterId, profile.Level, cancellationToken)
            .ConfigureAwait(false);
        var features = await LoadFeaturesAsync(characterId, cancellationToken).ConfigureAwait(false);
        return new CharacterEntryStorageRecord(
            accountId, characterId, profile, skillState, quests, progression, features);
    }

    /// <summary>
    /// Who the character is: four rows that must all be there, and what hangs off them.
    /// </summary>
    /// <remarks>
    /// The four used to be one join, and its innerness carried the rule: a character missing any
    /// of its detail rows produced no row at all and was refused as incomplete. Read separately
    /// they have to be refused deliberately, and in the same breath, because a character without
    /// them cannot be entered and saying which one is missing would tell a client something about
    /// the shape of the database.
    /// </remarks>
    private async ValueTask<CharacterProfileStorageRecord> LoadProfileAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        var row = await characters.FindEntryAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var s0 = row is null
            ? null
            : await subtype0.FindAsync(characterId, cancellationToken).ConfigureAwait(false);
        var s1 = s0 is null
            ? null
            : await subtype1.FindAsync(characterId, cancellationToken).ConfigureAwait(false);
        var flags = s1 is null
            ? (null, false)
            : await initFlags.FindOptionAsync(characterId, cancellationToken).ConfigureAwait(false);
        if (row is null || s0 is null || s1 is null || !flags.Item2)
        {
            throw new InvalidDataException(
                $"Character {characterId.Value} has incomplete required profile state.");
        }

        if (row.AccountId != accountId.Value)
        {
            throw new InvalidDataException(
                $"Character {characterId.Value} does not belong to account {accountId.Value}.");
        }

        var slots = await hotkeys.ReadAsync(characterId, cancellationToken).ConfigureAwait(false);
        if (slots.Any(value => value is < 0 or > ushort.MaxValue) || slots.Count > 256)
        {
            throw new InvalidDataException("Character hotkey state is out of range.");
        }

        RequireProfileInRange(s0, s1);
        var dimensionRows = new List<(int, long, int, int)>();
        foreach (var dimension in await dimensions.ReadAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (dimension.SortOrder < 0 || dimension.Key is < 0 or > uint.MaxValue
                || dimension.Value1 is < 0 or > byte.MaxValue
                || dimension.Value2 is < 0 or > byte.MaxValue)
            {
                throw new InvalidDataException("Character dimension state is out of range.");
            }

            dimensionRows.Add(
                (dimension.SortOrder, dimension.Key, dimension.Value1, dimension.Value2));
        }

        var flagValues = await dimensionFlags.ReadAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        if (new[] { flagValues.Item1, flagValues.Item2, flagValues.Item3, flagValues.Item4 }
            .Any(value => value is < 0 or > byte.MaxValue))
        {
            throw new InvalidDataException("Character dimension flags are out of range.");
        }

        return new CharacterProfileStorageRecord(
            checked((byte)row.Job), checked((byte)row.Level),
            checked((uint)row.CloneTitleItemId), row.Appearance, s0, s1,
            [.. slots.Select(value => checked((ushort)value))], flags.Item1,
            row.Experience, row.ExtraEquipSlotStat, dimensionRows, flagValues,
            checked((byte)row.PvpGrade), checked((byte)row.PvpRatingGrade),
            checked((byte)row.UserState));
    }

    private async ValueTask<CharacterSkillStorageRecord> LoadSkillsAsync(
        CharacterId characterId, byte job, CancellationToken cancellationToken)
    {
        var rows = new List<CharacterSkillStorageRow>();
        foreach (var skill in await skills.ReadEntryAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (skill.PageIndex is < 0 or > 1 || skill.Slot is < 0 or > short.MaxValue
                || skill.SkillId <= 0 || skill.Level is < 0 or > byte.MaxValue)
            {
                throw new InvalidDataException("Character skill state is out of range.");
            }

            rows.Add(new CharacterSkillStorageRow(
                checked((byte)skill.PageIndex), checked((short)skill.Slot), skill.SkillId,
                skill.Level, skill.ExtraValues));
        }

        var combos = new List<(byte, byte[])>();
        foreach (var page in await comboPages.ReadAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (page.PageIndex is < 0 or > 1)
            {
                throw new InvalidDataException("Dark Knight combo page is out of range.");
            }

            combos.Add((checked((byte)page.PageIndex), page.Body));
        }

        var shields = new List<(byte, int)>();
        foreach (var slot in await shieldDeck.ReadAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (slot.SlotIndex is < 0 or > 4 || slot.ShieldItemId <= 0)
            {
                throw new InvalidDataException("Knight Shield state is out of range.");
            }

            shields.Add((checked((byte)slot.SlotIndex), slot.ShieldItemId));
        }

        if (job != 9 && combos.Count != 0)
        {
            throw new InvalidDataException("Dark Knight combo state is inapplicable.");
        }

        if (job != 12 && shields.Count != 0)
        {
            throw new InvalidDataException("Knight Shield state is inapplicable.");
        }

        var bonuses = await characters.FindBonusPointsAsync(characterId, cancellationToken)
            .ConfigureAwait(false) ?? new BonusPointRow(0, 0);
        if (bonuses.BonusSp < 0 || bonuses.BonusTp < 0)
        {
            throw new InvalidDataException("Character bonus skill points cannot be negative.");
        }

        return new CharacterSkillStorageRecord(
            job, bonuses.BonusSp, bonuses.BonusTp, rows, combos, shields);
    }

    private async ValueTask<CharacterQuestStorageRecord> LoadQuestsAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var active = new List<CharacterActiveQuestStorageRow>();
        foreach (var quest in await activeQuests.ReadAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (quest.Slot is < 0 or > short.MaxValue || quest.QuestId <= 0
                || quest.TriggerValue < 0)
            {
                throw new InvalidDataException("Character active quest state is out of range.");
            }

            active.Add(new CharacterActiveQuestStorageRow(
                checked((short)quest.Slot), quest.QuestId, quest.TriggerValue));
        }

        var notifications = await questNotifications.ReadAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var flags = new List<(int, byte)>();
        foreach (var flag in await questClearFlags.ReadAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (flag.SlotIndex is < 0 or >= 30_000 || flag.FlagValue is < 0 or > byte.MaxValue)
            {
                throw new InvalidDataException("Quest clear flag state is out of range.");
            }

            flags.Add((flag.SlotIndex, checked((byte)flag.FlagValue)));
        }

        return new CharacterQuestStorageRecord(active, notifications, flags);
    }

    private async ValueTask<CharacterProgressionStorageRecord> LoadProgressionAsync(
        CharacterId characterId, byte level, CancellationToken cancellationToken)
    {
        var growth = await growthWeapons.ReadAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var cube = await crystalContract.FindAsync(characterId, cancellationToken)
            .ConfigureAwait(false) ?? new CrystalContractRow(0, 255);
        var champion = await initFlags.FindChampionBreakAsync(characterId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidDataException("Required character state is unavailable.");
        var gold = await goldLimitRows.FindAsync(characterId, cancellationToken)
            .ConfigureAwait(false) ?? new GoldLimitRow(0, 0);
        var goldLevel = goldLimits.UpgradeLevel(gold.CarryLimit, gold.AuctionLimit);

        var groupRows = await challengeGroups.ReadAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var claimed = (await challengeClaims.ReadAsync(characterId, cancellationToken)
            .ConfigureAwait(false)).ToHashSet();
        var groups = new List<CharacterDailyChallengeStorageGroup>();
        foreach (var group in groupRows)
        {
            if (group.GroupIndex is < 0 or >= 6 || group.GroupId < 0)
            {
                throw new InvalidDataException("Daily challenge group is out of range.");
            }

            var entries = new List<(int, int, int, int)>();
            foreach (var entry in await challengeEntries.ReadAsync(
                         characterId, group.GroupIndex, cancellationToken).ConfigureAwait(false))
            {
                if (entry.EntryIndex < 0 || entry.TrackLikeId < 0 || entry.ValueA < 0
                    || entry.ValueB < 0)
                {
                    throw new InvalidDataException("Daily challenge entry is out of range.");
                }

                entries.Add((entry.EntryIndex, entry.TrackLikeId, entry.ValueA, entry.ValueB));
            }

            groups.Add(new CharacterDailyChallengeStorageGroup(
                checked((byte)group.GroupIndex), group.GroupId, entries,
                claimed.Contains(group.GroupIndex)));
        }

        if (claimed.Except(groupRows.Select(value => value.GroupIndex)).Any())
        {
            throw new InvalidDataException("Daily challenge claim references a missing group.");
        }

        var tails = await challengeTails.ReadAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var schedules = (await dailySchedules.ReadAsync(characterId, cancellationToken)
                .ConfigureAwait(false))
            .Select(row => (row.SortOrder, row.ParamA, row.ModeOrState, row.ContentId, row.ParamB))
            .ToList();
        if (schedules.Count == 0)
        {
            schedules.AddRange([(0, 0, 0, 0, 0), (1, 0, 0, 0, 0), (2, 0, 0, 0, 0)]);
        }

        if (schedules.Count != 3
            || !schedules.Select(value => value.Item1).SequenceEqual([0, 1, 2]))
        {
            throw new InvalidDataException("Daily schedule must contain exactly slots 0, 1 and 2.");
        }

        var dungeons = new List<(int, int, int)>();
        foreach (var permission in await dungeonPermissions.ReadAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (permission.DungeonId <= 0 || permission.ClearState is < 0 or > byte.MaxValue)
            {
                throw new InvalidDataException("Dungeon progress is out of range.");
            }

            dungeons.Add((permission.SortOrder, permission.DungeonId, permission.ClearState));
        }

        var claimedRevive = await CounterAsync(characterId, ReviveCoinCounter, cancellationToken)
            .ConfigureAwait(false) > 0;
        var premiumUsage = await CounterAsync(characterId, LotteryDoubleCounter, cancellationToken)
            .ConfigureAwait(false);
        if (premiumUsage < 0)
        {
            throw new InvalidDataException("Character premium usage cannot be negative.");
        }

        return new CharacterProgressionStorageRecord(
            level, growth, cube.CubeType, cube.CubeGrade,
            champion.KeyId, champion.Mode, champion.Value, goldLevel,
            groups, tails, schedules, dungeons,
            [((byte)1, premiumUsage)], claimedRevive);
    }

    private async ValueTask<CharacterFeatureStorageRecord> LoadFeaturesAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var pets = new List<CharacterPetStorageRow>();
        foreach (var row in await creatures.ReadAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (row.SortOrder < 0 || row.CreatureKey <= 0 || row.ModeFlag is < 0 or > byte.MaxValue
                || row.Field04 is < 0 or > byte.MaxValue
                || row.Mode1Field0A is < 0 or > byte.MaxValue
                || row.Mode1Field0B is < 0 or > byte.MaxValue
                || row.FieldAfterValue is < 0 or > byte.MaxValue
                || row.TailFlag is < 0 or > byte.MaxValue || row.ProgressValue < 0)
            {
                throw new InvalidDataException("Creature runtime state is out of range.");
            }

            pets.Add(row);
        }

        var books = new List<CharacterTitleBookStorageRow>();
        foreach (var row in await titleBooks.ReadAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (row.Category is < 0 or > 4 || row.SlotIndex is < 0 or > ushort.MaxValue
                || row.Core.Length != 82)
            {
                throw new InvalidDataException("Title book entry is malformed.");
            }

            if (BinaryPrimitives.ReadInt32LittleEndian(row.Core.AsSpan(1)) <= 0)
            {
                throw new InvalidDataException("Title book item ID must be positive.");
            }

            books.Add(new CharacterTitleBookStorageRow(
                checked((byte)row.Category), row.SlotIndex, row.Core));
        }

        var completed = new List<CharacterAchievementStorageRow>();
        foreach (var row in await achievements.ReadAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (row.SortOrder < 0 || row.AchievementId <= 0
                || new[] { row.P1, row.P2, row.P3, row.P4 }
                    .Any(value => value is < 0 or > ushort.MaxValue))
            {
                throw new InvalidDataException("Achievement state is out of range.");
            }

            completed.Add(row);
        }

        var collection = new List<(int, int, int)>();
        foreach (var row in await collectionBoxes.ReadAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            // The index is bounded by the wire width, not by a fixed pair. This used to admit
            // only 3 and 4, which is a claim about the content archive rather than about the
            // row: the archive in use declares boxes 1 and 2, so every legitimate row it could
            // ever hold was rejected here.
            if (row.BoxIndex is < 0 or > byte.MaxValue || row.SlotIndex < 0 || row.ItemId <= 0)
            {
                throw new InvalidDataException("Collection box state is out of range.");
            }

            collection.Add((row.BoxIndex, row.SlotIndex, row.ItemId));
        }

        var expert = await subtype0.FindExpertJobAsync(characterId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidDataException("Required character state is unavailable.");
        var workshop = await expertJob.FindAsync(characterId, cancellationToken)
            .ConfigureAwait(false) ?? new ExpertJobRow(0, 0, 0, 0);
        var recipes = await expertRecipes.ReadAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        if (expert.Type is < 0 or > byte.MaxValue || expert.Experience < 0
            || workshop.GiveUpCount is < 0 or > ushort.MaxValue
            || workshop.DisjointMachineGrade < 0 || workshop.DisjointMachineEndurance < 0
            || workshop.EnchanterEndurance < 0
            || recipes.Any(value => value <= 0))
        {
            throw new InvalidDataException("Expert job state is out of range.");
        }

        var inbox = await LoadInboxAsync(characterId, cancellationToken).ConfigureAwait(false);
        var missions = new List<(int, int, int)>();
        foreach (var row in await pvpMissions.ReadAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (row.SortOrder < 0 || row.MissionId <= 0 || row.ProgressValue < 0)
            {
                throw new InvalidDataException("PvP mission state is out of range.");
            }

            missions.Add((row.SortOrder, row.MissionId, row.ProgressValue));
        }

        return new CharacterFeatureStorageRecord(
            pets, books, completed, collection,
            (expert.Type, expert.Experience, workshop.GiveUpCount, workshop.DisjointMachineGrade,
                workshop.DisjointMachineEndurance, workshop.EnchanterEndurance, recipes),
            inbox, missions);
    }

    /// <summary>
    /// The inbox: how much of it there is, and the page of it a character is shown.
    /// </summary>
    /// <remarks>
    /// Which messages count is a rule about mail rather than about either table: folder zero, not
    /// saved away, not deleted, and either unlimited or not yet expired. The oldest twenty of
    /// those are the page, and it is shown newest first -- so the page is chosen by one order and
    /// presented in its reverse, which is why both appear here.
    ///
    /// The two facts a row cannot settle for itself -- whether it is still live, and how long it
    /// has left -- are measured against the database's clock and come back with the row.
    ///
    /// Which twenty is settled from headers alone, and only those twenty are then read in full.
    /// An inbox has no size limit, so reading every letter's text to show twenty of them would
    /// cost more the more mail a character has.
    /// </remarks>
    private async ValueTask<CharacterMailboxStorageRecord> LoadInboxAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var recipients = await mailboxRecipients.ReadInboxAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var live = (await mailboxMessages.ReadHeadersAsync(
                [.. recipients.Select(value => value.MessageId).Distinct()], cancellationToken)
                .ConfigureAwait(false))
            .Where(header => header.Live)
            .ToArray();
        var chosen = live
            .OrderBy(header => header.CreatedAtUnixSeconds).ThenBy(header => header.MessageId)
            .Take(InboxPageSize)
            .ToArray();
        var page = (await mailboxMessages.ReadAsync(
                [.. chosen.Select(header => header.MessageId)], cancellationToken)
                .ConfigureAwait(false))
            .OrderByDescending(message => message.CreatedAtUnixSeconds)
            .ThenByDescending(message => message.MessageId)
            .ToArray();

        var byId = recipients.ToDictionary(value => value.MessageId);
        var rows = new List<CharacterMailboxMessageStorageRow>(page.Length);
        foreach (var message in page)
        {
            var recipient = byId[message.MessageId];
            rows.Add(new CharacterMailboxMessageStorageRow(
                message.MessageId, message.SenderCharacterId, message.SenderName,
                message.MailType, message.SourceProtocol, message.Title, message.Body,
                recipient.ReceivedGoldFlag == 0 ? message.Gold : 0,
                message.RemainingSeconds, message.CreatedAtUnixSeconds,
                recipient.SavedFlag != 0 ? 3 : recipient.ReadFlag != 0 ? 2 : 1,
                await mailboxAttachments.ReadAsync(message.MessageId, cancellationToken)
                    .ConfigureAwait(false)));
        }

        return new CharacterMailboxStorageRecord(live.Length, rows);
    }

    private async ValueTask<int> CounterAsync(
        CharacterId characterId, string key, CancellationToken cancellationToken) =>
        await dailyCounters.FindValueAsync(characterId, key, cancellationToken)
            .ConfigureAwait(false) ?? 0;

    /// <summary>What a character's two detail rows may hold.</summary>
    private static void RequireProfileInRange(
        CharacterSubtype0StorageRecord s0, CharacterSubtype1StorageRecord s1)
    {
        if (s0.Stamina is < 0 or > byte.MaxValue || s0.FatiguePenalty is < 0 or > uint.MaxValue
            || s0.ExpertJobType is < 0 or > byte.MaxValue || s0.ExpertJobExperience is < 0 or > uint.MaxValue
            || s0.HardcoreDeathCount is < 0 or > ushort.MaxValue || s0.UserStateBits is < 0 or > uint.MaxValue
            || s0.ChatBanEndTime is < 0 or > uint.MaxValue || s0.MoodValue is < 0 or > ushort.MaxValue
            || s0.PvpRankPoint is < 0 or > uint.MaxValue
            || new[] { s0.EventCharacter, s0.PrivateStore, s0.HardcoreMode, s0.HardcoreDead }
                .Any(value => value is < 0 or > 1)
            || new[] { s0.PremiumPcRoom, s0.Disguised, s0.ReturnUser, s0.LinkSlotEnabled }
                .Any(value => value is < 0 or > 1)
            || (s0.CreatureBuffer?.Length ?? 0) is not (0 or 8)
            || new[] { s0.CreatureField1, s0.CreatureField2, s0.CreatureField3, s0.CreatureField4,
                s0.ServerGroupId, s0.GuildLevel, s0.DisguiseKind, s0.ReturnUserFlag, s0.ChannelType,
                s0.LinkTypeA, s0.LinkTypeB, s0.ActionByte, s0.CostumeFlag, s0.AuraFlag,
                s0.PetDisplayFlag, s0.TitleDisplayFlag, s0.PvpWinStreak, s0.PvpLoseStreak,
                s0.TrailingByte }.Any(value => value is < 0 or > byte.MaxValue)
            || new[] { s0.FatigueUpdate, s0.ChannelDisplayMode, s0.EmotionIndex,
                s0.FatigueDisplayUpdate }.Any(value => value is < 0 or > ushort.MaxValue)
            || s0.PcRoomId is < 0 or > uint.MaxValue || s0.BlackCount is < 0 or > uint.MaxValue
            || s0.ChaosPoint is < 0 or > uint.MaxValue || s0.PvpStatA is < 0 or > uint.MaxValue
            || new[] { s1.HpMaximum, s1.MpMaximum, s1.PhysicalAttack, s1.PhysicalDefense,
                s1.MagicalAttack, s1.MagicalDefense, s1.MoveSpeed, s1.AttackSpeed,
                s1.CastSpeed, s1.InventoryLimit, s1.Progress1, s1.Progress2 }.Any(value => value < 0)
            || s1.SkillTreeIndex is < -1 or > 1)
        {
            throw new InvalidDataException("Character public profile state is out of range.");
        }

        if (new[] { s1.FireResistance, s1.WaterResistance, s1.DarkResistance, s1.LightResistance }
                .Any(value => value is < short.MinValue or > short.MaxValue)
            || new[] { s1.HpRegenSpeed, s1.MpRegenSpeed, s1.HitRecovery, s1.JumpPower,
                s1.QuestShopCount }.Any(value => value is < 0 or > ushort.MaxValue)
            || new[] { s1.CurrentHpMpPercent, s1.EquippedCreatureLevel, s1.FlagByte }
                .Any(value => value is < 0 or > byte.MaxValue)
            || s1.ManageLevel is < 0 or > byte.MaxValue
            || s1.Weight is < 0 or > uint.MaxValue || s1.NameTagItemId is < 0 or > uint.MaxValue
            || s1.NameTagExpireTime is < 0 or > uint.MaxValue
            || s1.GuildPowerWar is < 0 or > uint.MaxValue
            || s1.ServerTimestamp is < 0 or > uint.MaxValue)
        {
            throw new InvalidDataException("Character extended detail state is out of range.");
        }
    }
}
