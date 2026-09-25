using Dfo.Server.Abstractions.Notices;
using Dfo.Server.Data.Notices;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Quests;
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
                storage.Condition,
                storage.Settings,
                storage.Pvp,
                storage.ExpertJob,
                storage.DerivedStats,
                storage.Guild,
                new CharacterEntryExtrasState(
                    storage.SkillTreeIndex,
                    storage.NameTagItemId,
                    storage.NameTagExpireTime,
                    storage.ManageLevel,
                    storage.CharacterExperience,
                    storage.ExtraEquipmentSlotState,
                    storage.Dimensions,
                    storage.DimensionFlags,
                    []),
                new CharacterAppearanceCompatibilityState(1, [.. storage.AppearanceBlob]),
                storage.Settings.UserStateBits,
                storage.CloneTitleItemId,
                new CharacterPublicIdentityEntryState(
                    storage.PvpGrade,
                    storage.PvpRatingGrade,
                    storage.UserState)),
            new CharacterSettingsEntryState(
                [.. storage.HotkeySlots],
                storage.CharacterOptionBlob is null ? null : [.. storage.CharacterOptionBlob],
                storage.Settings.UserStateBits));
}

internal sealed class CharacterSkillQuestEntryProjection : ICharacterSkillQuestEntryProjection
{
    public (CharacterSkillsEntryState Skills, CharacterQuestEntryState Quests) Build(
        CharacterSkillStorageRecord skills,
        CharacterQuestStorageRecord quests)
    {
        var pages = CharacterSkillPages.Pages(skills.Skills);
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
            Dfo.Server.Application.Mail.MailboxProjection.Map(storage.Mailbox),
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
    IQuestContentRuleService questDefinitions,
    ICharacterProfileEntryProjection profiles,
    ICharacterSkillQuestEntryProjection skillQuests,
    ICharacterProgressionEntryProjection progression,
    ICharacterFeatureEntryProjection features,
    ICharacterDomainService characters, INoticeStateDomainService notices) : ICharacterEntryStateDomainService
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

        var stats = await characters.Get(characterId).BaseStatsAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Character no longer exists while building entry state.");
        var profile = profiles.Build(storage.Profile with
        {
            DerivedStats = CharacterBaseStatProjection.Apply(storage.Profile.DerivedStats, stats),
        });
        var skillQuest = skillQuests.Build(storage.Skill, storage.Quest);
        var specialRewardQuestIds = new List<int>();
        foreach (var clearFlag in storage.Quest.ClearFlags.Where(value => value.Value != 0))
        {
            var definition = await questDefinitions.FindAsync(clearFlag.SlotIndex, cancellationToken)
                .ConfigureAwait(false);
            if (definition?.HasSpecialRewardStatus == true)
                specialRewardQuestIds.Add(clearFlag.SlotIndex);
        }
        var publicState = profile.Public with
        {
            Extras = profile.Public.Extras with
            {
                SpecialRewardQuestIds = specialRewardQuestIds,
            },
        };
        return new CharacterEntryState(
            characterId,
            publicState,
            profile.Settings with
            {
                SeenNotices = await notices.GetSeenAsync(NoticeOwner.ForCharacter(characterId), NoticeGroup.Secondary, cancellationToken),
            },
            skillQuest.Skills,
            skillQuest.Quests,
            progression.Build(storage.Progression),
            features.Build(storage.Features));
    }
}
