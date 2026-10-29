using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Data.Mail;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Inventory;
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
    ICharacterConditionTable condition,
    ICharacterStatsTable statsRows,
    ICharacterSettingsTable settings,
    ICharacterPvpTable pvp,
    ICharacterHotkeyTable hotkeys,
    ICharacterDimensionTable dimensions,
    ICharacterSkillTable skills,
    ICharacterComboPageTable comboPages,
    ICharacterShieldDeckTable shieldDeck,
    ICharacterActiveQuestTable activeQuests,
    ICharacterQuestNotifyTable questNotifications,
    ICharacterQuestClearFlagTable questClearFlags,
    ICharacterGrowthWeaponTable growthWeapons,
    ICharacterGoldLimitTable goldLimitRows,
    ICharacterDailyChallengeTable challenges,
    ICharacterDailyChallengeEntryTable challengeEntries,
    ICharacterDailyChallengeTailTable challengeTails,
    ICharacterDailyScheduleTable dailySchedules,
    ICharacterDungeonDifficultyDomainService dungeonPermissions,
    IReviveCoinQueryDomainService reviveCoins,
    ICharacterCreatureTable creatures,
    ICharacterTitleBookTable titleBooks,
    ICharacterAchievementTable achievements,
    ICharacterCollectionBoxTable collectionBoxes,
    ICharacterExpertJobTable expertJob,
    ICharacterExpertRecipeTable expertRecipes,
    ICharacterPvpMissionTable pvpMissions,
    IMailboxQueryDomainService mailbox,
    IGoldLimitRuleService goldLimits, IPremiumServiceUsageDomainService premiumServices, ICharacterSkillBuildTable skillBuilds,
    Dfo.Server.Application.Marriage.IMarriageRelations? marriageRelations = null) : ICharacterEntryStateRepository
{
    /// <summary>How many of the inbox a character is shown on entry.</summary>



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
    /// <summary>
    /// Each table's row as the concept it holds.
    /// </summary>
    /// <remarks>
    /// These used to be two methods, each flattening several rows into one record laid out in a
    /// protocol segment's order -- an order this layer has no reason to know. Reading them apart
    /// costs nothing here and means the encoder is the only thing that decides what goes into the
    /// packet and in what sequence.
    /// </remarks>
    private static CharacterConditionStorageRecord Condition(CharacterConditionRow c) =>
        new(c.Stamina, c.FatiguePenalty, c.EventCharacter, c.PrivateStore,
            c.HardcoreMode, c.HardcoreDead, c.HardcoreDeathCount,
            c.ChatBanEndTime, c.MoodValue,
            c.CreatureField1, c.CreatureField2, c.CreatureField3, c.CreatureField4,
            c.CreatureBuffer, c.PcRoomId, c.PremiumPcRoom, c.ServerGroupId, c.BlackCount,
            c.ChaosPoint, c.DisguiseKind, c.Disguised, c.FatigueUpdate,
            c.ReturnUserFlag, c.ChannelDisplayMode, c.ChannelType, c.ReturnUser,
            c.LinkSlotEnabled, c.LinkTypeA, c.LinkTypeB, c.EmotionIndex, c.ActionByte,
            c.FatigueDisplayUpdate, c.CostumeFlag, c.AuraFlag, c.PetDisplayFlag,
            c.TitleDisplayFlag, c.TrailingByte);

    private static CharacterDerivedStatsStorageRecord DerivedStats(CharacterStatsRow s) =>
        new(s.HpMaximum, s.MpMaximum, s.PhysicalAttack, s.PhysicalDefense, s.MagicalAttack,
            s.MagicalDefense, s.MoveSpeed, s.AttackSpeed, s.CastSpeed, s.InventoryLimit,
            s.Progress1, s.Progress2, s.FireResistance, s.WaterResistance,
            s.DarkResistance, s.LightResistance, s.HpRegenSpeed, s.MpRegenSpeed, s.HitRecovery,
            s.JumpPower, s.Weight, s.CurrentHpMpPercent, s.EquippedCreatureLevel,
            s.EquipListTrailing, s.FlagByte, s.ServerTimestamp, s.QuestShopCount);

    private async ValueTask<CharacterProfileStorageRecord> LoadProfileAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        var row = await characters.FindEntryAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var conditionRow = row is null
            ? null
            : await condition.FindAsync(characterId, cancellationToken).ConfigureAwait(false);
        var statsRow = conditionRow is null
            ? null
            : await statsRows.FindAsync(characterId, cancellationToken).ConfigureAwait(false);
        var settingsRow = statsRow is null
            ? null
            : await settings.FindAsync(characterId, cancellationToken).ConfigureAwait(false);
        var pvpRow = settingsRow is null
            ? null
            : await pvp.FindAsync(characterId, cancellationToken).ConfigureAwait(false);
        var jobRow = pvpRow is null
            ? null
            : await expertJob.FindAsync(characterId, cancellationToken).ConfigureAwait(false);
        if (row is null || conditionRow is null || statsRow is null || settingsRow is null
            || pvpRow is null || jobRow is null)
        {
            throw new InvalidDataException(
                $"Character {characterId.Value} has incomplete required profile state.");
        }

        // The two records the rest of the load already worked with, rebuilt from the tables the
        // ruling cut them into. Nothing above this line changes shape; only where it comes from.
        var conditionState = Condition(conditionRow);
        var settingsState = new CharacterSettingsStorageRecord(settingsRow.UserStateBits);
        var pvpState = new CharacterPvpStandingStorageRecord(
            pvpRow.PvpRankPoint, pvpRow.PvpStatA, pvpRow.PvpWinStreak, pvpRow.PvpLoseStreak, []);
        var expertJobState = new CharacterExpertJobStorageRecord(
            jobRow.ExpertJobType, jobRow.ExpertJobExperience);
        var statsState = DerivedStats(statsRow);
        var guildState = new CharacterGuildStandingStorageRecord(row.GuildLevel, row.GuildPowerWar);
        var flags = (settingsRow.OptionBlob, true);

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

        var builds = await skillBuilds.ReadAsync(characterId, cancellationToken).ConfigureAwait(false);
        var projectedTree = builds.IsUnlocked(new(1)) ? builds.Active.Value : -1;
        RequireProfileInRange(conditionState, settingsState, pvpState, expertJobState,
            statsState, guildState, projectedTree,
            row.NameTagItemId, row.NameTagExpireTime, row.ManageLevel);
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

        var flagValues = (conditionRow.DimensionFlag1, conditionRow.DimensionFlag2,
            conditionRow.DimensionFlag3, conditionRow.DimensionFlag4);
        if (new[] { flagValues.Item1, flagValues.Item2, flagValues.Item3, flagValues.Item4 }
            .Any(value => value is < 0 or > byte.MaxValue))
        {
            throw new InvalidDataException("Character dimension flags are out of range.");
        }

        return new CharacterProfileStorageRecord(
            checked((byte)row.Job), checked((byte)row.Level),
            checked((uint)row.CloneTitleItemId),
            conditionState, settingsState, pvpState, expertJobState, statsState, guildState,
            projectedTree, row.NameTagItemId, row.NameTagExpireTime, row.ManageLevel,
            [.. slots.Select(value => checked((ushort)value))], flags.Item1,
            row.Experience, row.ExtraEquipSlotStat, dimensionRows, flagValues,
            checked((byte)pvpRow.PvpGrade), checked((byte)pvpRow.PvpRatingGrade),
            checked((byte)row.UserState));
    }

    private async ValueTask<CharacterSkillStorageRecord> LoadSkillsAsync(
        CharacterId characterId, byte job, CancellationToken cancellationToken)
    {
        // The same reading a character gives its own rows when it is asked what it has left to
        // spend. Shared rather than repeated: the two have to agree exactly, and a divergence
        // between them would show up only as a skill-point total that looks reasonable.
        var rows = CharacterSkillPages.Rows(
            await skills.ReadEntryAsync(characterId, cancellationToken).ConfigureAwait(false));

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
        var state = await condition.FindAsync(characterId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidDataException("Required character state is unavailable.");
        var cube = (CubeType: state.CrystalCubeType, CubeGrade: state.CrystalCubeGrade);
        var champion = (KeyId: state.ChampionBreakKeyId, Mode: state.ChampionBreakMode,
            Value: state.ChampionBreakValue);
        var gold = await goldLimitRows.FindAsync(characterId, cancellationToken)
            .ConfigureAwait(false) ?? new GoldLimitRow(0, 0);
        var goldLevel = goldLimits.UpgradeLevel(gold.CarryLimit, gold.AuctionLimit);

        var groupRows = await challenges.ReadAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var claimed = groupRows.Where(group => group.Claimed)
            .Select(group => group.GroupIndex).ToHashSet();
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

        var recordedDifficulties = await dungeonPermissions.GetRecordedAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        // Couple-map visibility follows this character's relation, never the account-wide permanent unlock ledger.
        var visibleDifficulties = recordedDifficulties.Where(x => x.DungeonId != World.CoupleDungeonRule.DungeonId).ToList();
        if (marriageRelations is not null && await marriageRelations.FindAsync(characterId, cancellationToken).ConfigureAwait(false)
            is { Stage: Dfo.Server.Data.Marriage.MarriageStage.Married }) visibleDifficulties.Add(new(World.CoupleDungeonRule.DungeonId, World.CoupleDungeonRule.MarriedDifficulty));
        var dungeons = visibleDifficulties.Select((value, index) =>
            (index, value.DungeonId, (int)value.Maximum)).ToArray();

        var claimedRevive = await reviveCoins.ClaimedAsync(characterId, cancellationToken).ConfigureAwait(false);
        var premiumUsage = await premiumServices.ReadAsync(characterId, cancellationToken).ConfigureAwait(false);

        return new CharacterProgressionStorageRecord(
            level, growth, cube.CubeType, cube.CubeGrade,
            champion.KeyId, champion.Mode, champion.Value, goldLevel,
            groups, tails, schedules, dungeons,
            premiumUsage.Select(slot => (slot.SlotIndex, slot.UsedCount)).ToArray(), claimedRevive);
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

        var workshop = await expertJob.FindAsync(characterId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidDataException("Required character state is unavailable.");
        // The expert job's experience is the public profile's; RequireProfileInRange checks it.
        var recipes = await expertRecipes.ReadAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        if (workshop.ExpertJobType is < 0 or > byte.MaxValue
            || workshop.GiveUpCount is < 0 or > ushort.MaxValue
            || workshop.DisjointMachineGrade < 0 || workshop.DisjointMachineEndurance < 0
            || workshop.EnchanterEndurance < 0
            || recipes.Any(value => value <= 0))
        {
            throw new InvalidDataException("Expert job state is out of range.");
        }

        var inbox = await mailbox.ReadStorageAsync(characterId, cancellationToken).ConfigureAwait(false);
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
            (workshop.ExpertJobType, workshop.GiveUpCount, workshop.DisjointMachineGrade,
                workshop.DisjointMachineEndurance, workshop.EnchanterEndurance, recipes),
            inbox, missions);
    }


    /// <summary>
    /// What a character's stored profile may hold, given the widths the packet gives each field.
    /// </summary>
    /// <remarks>
    /// A row too wide for its field is refused here, at load, rather than at encode. That is
    /// deliberate and predates the concepts these arguments are now grouped into: a character whose
    /// stored state cannot be sent is refused entry with one message, instead of getting most of
    /// the way in and failing while a packet is half written.
    ///
    /// It does mean this layer knows some widths. That is the one place it does, it is a refusal
    /// rather than a conversion, and moving it to the encoder would change when the refusal
    /// happens -- which is a decision for whoever moves it, not a side effect of regrouping.
    ///
    /// Four of these checks were lost once, in the change that regrouped these arguments: the four
    /// fields that left the flattened record for somewhere else -- the name tag's two, the guild's
    /// power war, and the manage level -- were the four whose checks had nothing left to hang off.
    /// Nothing went red, because losing a refusal only shows when a value is out of range, and no
    /// fixture has one. They are back, and each is now reachable from the arguments this takes.
    /// </remarks>
    private static void RequireProfileInRange(
        CharacterConditionStorageRecord c,
        CharacterSettingsStorageRecord settings,
        CharacterPvpStandingStorageRecord pvp,
        CharacterExpertJobStorageRecord job,
        CharacterDerivedStatsStorageRecord stats,
        CharacterGuildStandingStorageRecord guild,
        int skillTreeIndex,
        long nameTagItemId,
        long nameTagExpireTime,
        int manageLevel)
    {
        if (c.Stamina is < 0 or > byte.MaxValue || c.FatiguePenalty is < 0 or > uint.MaxValue
            || job.Type is < 0 or > byte.MaxValue || job.Experience is < 0 or > uint.MaxValue
            || c.HardcoreDeathCount is < 0 or > ushort.MaxValue
            || settings.UserStateBits is < 0 or > uint.MaxValue
            || c.ChatBanEndTime is < 0 or > uint.MaxValue || c.MoodValue is < 0 or > ushort.MaxValue
            || pvp.RankPoint is < 0 or > uint.MaxValue
            || new[] { c.EventCharacter, c.PrivateStore, c.HardcoreMode, c.HardcoreDead }
                .Any(value => value is < 0 or > 1)
            || new[] { c.PremiumPcRoom, c.Disguised, c.ReturnUser, c.LinkSlotEnabled }
                .Any(value => value is < 0 or > 1)
            || (c.CreatureBuffer?.Length ?? 0) is not (0 or 8)
            || new[] { c.CreatureField1, c.CreatureField2, c.CreatureField3, c.CreatureField4,
                c.ServerGroupId, guild.Level, c.DisguiseKind, c.ReturnUserFlag, c.ChannelType,
                c.LinkTypeA, c.LinkTypeB, c.ActionByte, c.CostumeFlag, c.AuraFlag,
                c.PetDisplayFlag, c.TitleDisplayFlag, pvp.WinStreak, pvp.LoseStreak,
                c.TrailingByte }.Any(value => value is < 0 or > byte.MaxValue)
            || new[] { c.FatigueUpdate, c.ChannelDisplayMode, c.EmotionIndex,
                c.FatigueDisplayUpdate }.Any(value => value is < 0 or > ushort.MaxValue)
            || c.PcRoomId is < 0 or > uint.MaxValue || c.BlackCount is < 0 or > uint.MaxValue
            || c.ChaosPoint is < 0 or > uint.MaxValue || pvp.StatA is < 0 or > uint.MaxValue
            || new[] { stats.HpMaximum, stats.MpMaximum, stats.PhysicalAttack,
                stats.PhysicalDefense, stats.MagicalAttack, stats.MagicalDefense, stats.MoveSpeed,
                stats.AttackSpeed, stats.CastSpeed, stats.InventoryLimit, stats.Progress1,
                stats.Progress2 }.Any(value => value < 0)
            || skillTreeIndex is < -1 or > 1)
        {
            throw new InvalidDataException("Character public profile state is out of range.");
        }

        if (new[] { stats.FireResistance, stats.WaterResistance, stats.DarkResistance,
                stats.LightResistance }.Any(value => value is < short.MinValue or > short.MaxValue)
            || new[] { stats.HpRegenSpeed, stats.MpRegenSpeed, stats.HitRecovery, stats.JumpPower,
                stats.QuestShopCount }.Any(value => value is < 0 or > ushort.MaxValue)
            || new[] { stats.CurrentHpMpPercent, stats.EquippedCreatureLevel, stats.FlagByte }
                .Any(value => value is < 0 or > byte.MaxValue)
            || manageLevel is < 0 or > byte.MaxValue
            || stats.Weight is < 0 or > uint.MaxValue
            || nameTagItemId is < 0 or > uint.MaxValue
            || nameTagExpireTime is < 0 or > uint.MaxValue
            || guild.PowerWar is < 0 or > uint.MaxValue
            || stats.ServerTimestamp is < 0 or > uint.MaxValue)
        {
            throw new InvalidDataException("Character extended detail state is out of range.");
        }
    }
}
