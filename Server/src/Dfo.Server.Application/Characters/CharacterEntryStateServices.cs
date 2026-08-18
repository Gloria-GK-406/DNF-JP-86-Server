using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterProfileEntryProjection : ICharacterProfileEntryProjection
{
    public (CharacterPublicEntryState Public, CharacterSettingsEntryState Settings) Build(
        CharacterProfileStorageRecord storage) =>
        (
            new CharacterPublicEntryState(
                new CharacterSubtype0EntryState(
                    checked((byte)storage.Subtype0.Stamina),
                    checked((uint)storage.Subtype0.FatiguePenalty),
                    storage.Subtype0.EventCharacter != 0,
                    storage.Subtype0.PrivateStore != 0,
                    checked((byte)storage.Subtype0.ExpertJobType),
                    checked((uint)storage.Subtype0.ExpertJobExperience),
                    storage.Subtype0.HardcoreMode != 0,
                    storage.Subtype0.HardcoreDead != 0,
                    checked((ushort)storage.Subtype0.HardcoreDeathCount),
                    checked((uint)storage.Subtype0.UserStateBits),
                    checked((uint)storage.Subtype0.ChatBanEndTime),
                    checked((ushort)storage.Subtype0.MoodValue),
                    checked((uint)storage.Subtype0.PvpRankPoint),
                    checked((byte)storage.Subtype0.CreatureField1),
                    checked((byte)storage.Subtype0.CreatureField2),
                    checked((byte)storage.Subtype0.CreatureField3),
                    checked((byte)storage.Subtype0.CreatureField4),
                    [.. storage.Subtype0.CreatureBuffer ?? []],
                    checked((uint)storage.Subtype0.PcRoomId),
                    storage.Subtype0.PremiumPcRoom != 0,
                    checked((byte)storage.Subtype0.ServerGroupId),
                    checked((uint)storage.Subtype0.BlackCount),
                    checked((byte)storage.Subtype0.GuildLevel),
                    checked((uint)storage.Subtype0.ChaosPoint),
                    checked((byte)storage.Subtype0.DisguiseKind),
                    storage.Subtype0.Disguised != 0,
                    checked((ushort)storage.Subtype0.FatigueUpdate),
                    checked((byte)storage.Subtype0.ReturnUserFlag),
                    checked((ushort)storage.Subtype0.ChannelDisplayMode),
                    checked((byte)storage.Subtype0.ChannelType),
                    storage.Subtype0.ReturnUser != 0,
                    storage.Subtype0.LinkSlotEnabled != 0,
                    checked((byte)storage.Subtype0.LinkTypeA),
                    checked((byte)storage.Subtype0.LinkTypeB),
                    checked((ushort)storage.Subtype0.EmotionIndex),
                    checked((byte)storage.Subtype0.ActionByte),
                    checked((ushort)storage.Subtype0.FatigueDisplayUpdate),
                    checked((byte)storage.Subtype0.CostumeFlag),
                    checked((byte)storage.Subtype0.AuraFlag),
                    checked((byte)storage.Subtype0.PetDisplayFlag),
                    checked((byte)storage.Subtype0.TitleDisplayFlag),
                    checked((uint)storage.Subtype0.PvpStatA),
                    checked((byte)storage.Subtype0.PvpWinStreak),
                    checked((byte)storage.Subtype0.PvpLoseStreak),
                    checked((byte)storage.Subtype0.TrailingByte)),
                new CharacterSubtype1EntryState(
                    storage.Subtype1.HpMaximum,
                    storage.Subtype1.MpMaximum,
                    storage.Subtype1.PhysicalAttack,
                    storage.Subtype1.PhysicalDefense,
                    storage.Subtype1.MagicalAttack,
                    storage.Subtype1.MagicalDefense,
                    storage.Subtype1.MoveSpeed,
                    storage.Subtype1.AttackSpeed,
                    storage.Subtype1.CastSpeed,
                    storage.Subtype1.InventoryLimit,
                    storage.Subtype1.SkillTreeIndex,
                    storage.Subtype1.Progress1,
                    storage.Subtype1.Progress2,
                    checked((short)storage.Subtype1.FireResistance),
                    checked((short)storage.Subtype1.WaterResistance),
                    checked((short)storage.Subtype1.DarkResistance),
                    checked((short)storage.Subtype1.LightResistance),
                    checked((ushort)storage.Subtype1.HpRegenSpeed),
                    checked((ushort)storage.Subtype1.MpRegenSpeed),
                    checked((ushort)storage.Subtype1.HitRecovery),
                    checked((ushort)storage.Subtype1.JumpPower),
                    checked((uint)storage.Subtype1.Weight),
                    checked((byte)storage.Subtype1.CurrentHpMpPercent),
                    checked((uint)storage.Subtype1.NameTagItemId),
                    checked((uint)storage.Subtype1.NameTagExpireTime),
                    checked((byte)storage.Subtype1.EquippedCreatureLevel),
                    storage.Subtype1.EquipListTrailing,
                    checked((byte)storage.Subtype1.FlagByte),
                    checked((uint)storage.Subtype1.GuildPowerWar),
                    checked((uint)storage.Subtype1.ServerTimestamp),
                    checked((ushort)storage.Subtype1.QuestShopCount),
                    checked((uint)storage.CharacterExperience),
                    checked((byte)storage.ExtraEquipmentSlotState),
                    storage.Dimensions.OrderBy(value => value.SortOrder)
                        .Select(value => new CharacterDimensionEntryState(
                            checked((uint)value.Key), checked((byte)value.Value1), checked((byte)value.Value2)))
                        .ToArray(),
                    new CharacterDimensionFlagsState(
                        checked((byte)storage.DimensionFlags.Flag1),
                        checked((byte)storage.DimensionFlags.Flag2),
                        checked((byte)storage.DimensionFlags.Flag3),
                        checked((byte)storage.DimensionFlags.Flag4)),
                    checked((byte)storage.Subtype1.ManageLevel),
                    (storage.Subtype1.PvpResults
                        ?? throw new InvalidDataException("Subtype-1 PvP results were not loaded."))
                        .Select(value => new CharacterPvpResultState(
                            checked((uint)value.Value32), checked((ushort)value.Value16A),
                            checked((ushort)value.Value16B)))
                        .ToArray(),
                    []),
                new CharacterAppearanceCompatibilityState(1, [.. storage.AppearanceBlob]),
                checked((uint)storage.Subtype0.UserStateBits),
                storage.CloneTitleItemId,
                new CharacterPublicIdentityEntryState(
                    storage.PvpGrade,
                    storage.PvpRatingGrade,
                    storage.UserState)),
            new CharacterSettingsEntryState(
                [.. storage.HotkeySlots],
                storage.CharacterOptionBlob is null ? null : [.. storage.CharacterOptionBlob],
                checked((uint)storage.Subtype0.UserStateBits)));
}

internal sealed class CharacterSkillQuestEntryProjection : ICharacterSkillQuestEntryProjection
{
    public (CharacterSkillsEntryState Skills, CharacterQuestEntryState Quests) Build(
        CharacterSkillStorageRecord skills,
        CharacterQuestStorageRecord quests)
    {
        var pages = skills.Skills
            .GroupBy(value => value.PageIndex)
            .OrderBy(group => group.Key)
            .Select(group => new CharacterSkillPageState(
                group.Key,
                group.OrderBy(value => value.Slot)
                    .Select(value => new CharacterSkillState(
                        value.Slot,
                        checked((uint)value.SkillId),
                        checked((byte)value.Level),
                        [.. value.ExtraValues]))
                    .ToArray()))
            .ToArray();
        var combos = skills.Job == 9
            ? skills.DarkKnightComboPages.OrderBy(value => value.PageIndex)
                .Select(value => new DarkKnightComboEntryState(value.PageIndex, [.. value.Body]))
                .ToArray()
            : null;
        var deck = skills.Job == 12
            ? Enumerable.Range(0, 5)
                .Select(slot => skills.KnightShieldSlots
                    .FirstOrDefault(value => value.SlotIndex == slot).ShieldItemId)
                .ToArray()
            : null;
        var activeQuests = quests.ActiveQuests.OrderBy(value => value.Slot)
            .Select(value => new CharacterQuestProgressState(
                value.Slot, value.QuestId, value.TriggerValue))
            .ToArray();
        var visibility = new byte[30_000];
        foreach (var flag in quests.ClearFlags)
        {
            visibility[flag.SlotIndex] = flag.Value;
        }

        return (
            new CharacterSkillsEntryState(
                pages,
                [],
                new CharacterSkillPointBasisState(skills.BonusSp, skills.BonusTp),
                combos,
                deck),
            new CharacterQuestEntryState(
                activeQuests,
                quests.ClearFlags.Where(value => value.Value != 0)
                    .Select(value => value.SlotIndex).Order().ToArray(),
                quests.NotificationQuestIds.Order().ToArray(),
                visibility));
    }
}

internal sealed class CharacterProgressionEntryProjection : ICharacterProgressionEntryProjection
{
    public CharacterProgressionEntryState Build(CharacterProgressionStorageRecord storage)
    {
        var claimFlags = new byte[6];
        foreach (var group in storage.DailyChallenges.Where(value => value.Claimed))
        {
            claimFlags[group.GroupIndex] = 1;
        }

        return new CharacterProgressionEntryState(
            storage.GrowthWeaponStages.Select(checkedValue => checked((byte)checkedValue)).ToArray(),
            checked((byte)storage.CubeType),
            checked((byte)storage.CubeGrade),
            storage.ChampionBreakKey,
            checked((byte)storage.ChampionBreakMode),
            storage.ChampionBreakValue,
            checked((byte)storage.GoldLimitUpgradeLevel),
            0,
            0,
            storage.DailyReviveCoinClaimed,
            storage.Level,
            storage.DailyChallenges.OrderBy(value => value.GroupIndex)
                .Select(group => new CharacterDailyChallengeGroupState(
                    group.GroupIndex,
                    checked((uint)group.GroupId),
                    group.Entries.OrderBy(value => value.EntryIndex)
                        .Select(value => new CharacterDailyChallengeItemState(
                            checked((uint)value.TrackId),
                            checked((uint)value.ValueA),
                            checked((uint)value.ValueB)))
                        .ToArray(),
                    group.Claimed))
                .ToArray(),
            claimFlags,
            storage.DailyChallengeTailIds.Select(value => checked((uint)value)).ToArray(),
            storage.Schedules.OrderBy(value => value.SortOrder)
                .Select((value, index) => new CharacterScheduleCompatibilityState(
                    checked((byte)index), value.ParamA, value.ModeOrState, value.ContentId, value.ParamB))
                .ToArray(),
            storage.Dungeons.OrderBy(value => value.SortOrder)
                .Select(value => new CharacterDungeonProgressState(
                    value.DungeonId, checked((byte)value.ClearState)))
                .ToArray(),
            storage.PremiumUsageSlots.OrderBy(value => value.SlotIndex)
                .Select(value => new CharacterPremiumUsageSlotState(value.SlotIndex, value.UsedCount))
                .ToArray());
    }
}

internal sealed class CharacterFeatureEntryProjection : ICharacterFeatureEntryProjection
{
    public CharacterFeaturesEntryState Build(CharacterFeatureStorageRecord storage)
    {
        var titleBooks = Enumerable.Range(0, 5)
            .Select(category => new CharacterTitleBookCategoryState(
                checked((byte)category),
                0,
                0,
                storage.TitleBooks.Where(value => value.Category == category)
                    .OrderBy(value => value.SlotIndex)
                    .Select(DecodeTitleBookEntry)
                    .ToArray()))
            .ToArray();
        // Whatever boxes this character has stored progress for. Which boxes *exist* is a content
        // question and is settled later, in SelectCharacterPreparationService, against the archive's
        // collect-box table; inventing an index set here cannot know that and previously produced a
        // pair the archive does not declare.
        var boxes = storage.CollectionSlots
            .GroupBy(value => value.BoxIndex)
            .OrderBy(group => group.Key)
            .Select(group => new CharacterCollectionBoxProgressState(
                checked((byte)group.Key),
                group.OrderBy(value => value.SlotIndex)
                    .Select(value => new CharacterCollectionBoxSlotState(value.SlotIndex, value.ItemId))
                    .ToArray()))
            .ToArray();
        return new CharacterFeaturesEntryState(
            storage.Pets.OrderBy(value => value.SortOrder)
                .Select(value => new CharacterPetRuntimeState(
                    value.CreatureKey,
                    value.Field04,
                    checked((byte)value.ModeFlag),
                    value.ProgressValue,
                    value.Mode1Field0A,
                    value.Mode1Field0B,
                    value.FieldAfterValue,
                    [.. value.CreatureText],
                    checked((byte)value.TailFlag)))
                .ToArray(),
            new CharacterCollectionsEntryState(
                titleBooks,
                storage.Achievements.OrderBy(value => value.SortOrder)
                    .Select(value => new CharacterAchievementProgressState(
                        value.AchievementId,
                        checked((ushort)value.P1),
                        checked((ushort)value.P2),
                        checked((ushort)value.P3),
                        checked((ushort)value.P4)))
                    .ToArray(),
                boxes),
            new CharacterExpertJobEntryState(
                checked((byte)storage.ExpertJob.ProfessionType),
                storage.ExpertJob.Experience,
                checked((ushort)storage.ExpertJob.GiveupCount),
                storage.ExpertJob.DisjointMachineGrade,
                storage.ExpertJob.DisjointMachineEndurance,
                storage.ExpertJob.EnchanterEndurance,
                storage.ExpertJob.RecipeIds.Order().ToArray()),
            new CharacterMailboxEntryState(
                Math.Max(0, storage.Mailbox.TotalCount - storage.Mailbox.PageMessages.Count),
                storage.Mailbox.PageMessages.OrderByDescending(value => value.MessageId)
                    .Select(value => new CharacterMailboxMessageEntryState(
                        value.MessageId,
                        value.SenderCharacterId,
                        System.Text.Encoding.UTF8.GetBytes(value.SenderName),
                        value.MailType,
                        value.SourceProtocol,
                        System.Text.Encoding.UTF8.GetBytes(value.Title),
                        System.Text.Encoding.UTF8.GetBytes(value.Body),
                        value.Gold,
                        value.RemainingSeconds,
                        value.CreatedAtUnixSeconds,
                        checked((byte)value.LetterState),
                        value.Attachments.OrderBy(attachment => attachment.Ordinal)
                            .Select(attachment => new CharacterMailboxAttachmentEntryState(
                                attachment.AttachmentId,
                                attachment.Ordinal,
                                checked((byte)attachment.ItemType),
                                attachment.SourceListType,
                                attachment.SourceSlotIndex,
                                attachment.SourceItemUid,
                                attachment.ItemTemplateId,
                                attachment.ItemKind,
                                attachment.ItemCount,
                                attachment.InstanceValue,
                                attachment.Durability,
                                attachment.SealFlag,
                                attachment.OptionValue,
                                attachment.ExpireTime,
                                attachment.Marker16,
                                attachment.PetSerialOrHandle,
                                [.. attachment.ItemCore],
                                attachment.DetailJson))
                            .ToArray()))
                    .ToArray()),
            storage.PvpMissions.OrderBy(value => value.SortOrder)
                .Select(value => new CharacterPvpMissionEntryState(value.MissionId, value.Progress))
                .ToArray());
    }

    private static CharacterTitleBookEntryState DecodeTitleBookEntry(
        CharacterTitleBookStorageRow row)
    {
        var core = row.ItemCore;
        return new CharacterTitleBookEntryState(
            checked((ushort)row.SlotIndex),
            BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1)),
            BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5)),
            core[9],
            BinaryPrimitives.ReadUInt16LittleEndian(core.AsSpan(10)),
            core[12],
            BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(13)),
            core[17],
            core[18],
            BinaryPrimitives.ReadUInt16LittleEndian(core.AsSpan(19)));
    }
}

internal sealed class CharacterEntryStateDomainService(
    ICharacterEntryStateRepository repository,
    IQuestDefinitionSource questDefinitions,
    ICharacterProfileEntryProjection profiles,
    ICharacterSkillQuestEntryProjection skillQuests,
    ICharacterProgressionEntryProjection progression,
    ICharacterFeatureEntryProjection features) : ICharacterEntryStateDomainService
{
    public async ValueTask<CharacterEntryState> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var storage = await repository.LoadAsync(accountId, characterId, cancellationToken)
            .ConfigureAwait(false);
        if (storage.AccountId != accountId || storage.CharacterId != characterId)
        {
            throw new InvalidDataException("Character entry storage returned a different owner.");
        }

        var profile = profiles.Build(storage.Profile);
        var skillQuest = skillQuests.Build(storage.Skill, storage.Quest);
        var specialRewardQuestIds = new List<uint>();
        foreach (var clearFlag in storage.Quest.ClearFlags.Where(value => value.Value != 0))
        {
            var definition = await questDefinitions.GetAsync(clearFlag.SlotIndex, cancellationToken)
                .ConfigureAwait(false);
            if (definition?.HasSpecialRewardStatus == true)
                specialRewardQuestIds.Add(checked((uint)clearFlag.SlotIndex));
        }
        var publicState = profile.Public with
        {
            Subtype1 = profile.Public.Subtype1 with
            {
                SpecialRewardQuestIds = specialRewardQuestIds,
            },
        };
        return new CharacterEntryState(
            characterId,
            publicState,
            profile.Settings,
            skillQuest.Skills,
            skillQuest.Quests,
            progression.Build(storage.Progression),
            features.Build(storage.Features));
    }
}
