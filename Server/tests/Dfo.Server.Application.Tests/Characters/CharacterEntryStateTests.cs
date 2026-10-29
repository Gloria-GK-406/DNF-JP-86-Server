using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class CharacterEntryStateTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CharacterEntryState_DerivesCompleteTypedViewsFromRawSliceStorage(bool blackDiamond)
    {
        var accountId = new AccountId(61);
        var characterId = new CharacterId(71);
        var service = new CharacterEntryStateDomainService(
            new StubRepository(Storage(accountId, characterId)),
            new QuestContentRuleService(new StubQuestDefinitions([200]), new NoQuestParameters()),
            new CharacterProfileEntryProjection(),
            new CharacterSkillQuestEntryProjection(),
            new CharacterProgressionEntryProjection(),
            new CharacterFeatureEntryProjection(),
            Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain(
                new Dfo.Server.Application.Tests.Support.CurrentCharacterStub { Stats = new CharacterBaseStats(PhysicalAttack: 123) }), new Dfo.Server.Application.Tests.Support.NoticeStateStub(),
            new Dfo.Server.Application.Tests.Support.CardPremiums(accountId.Value) { Enabled = blackDiamond }, new Dfo.Server.Application.Tests.Support.NoHerasEntries());

        var result = await service.LoadAsync(accountId, characterId, CancellationToken.None);

        Assert.Equal(new byte[] { 15,16 }, result.Settings.SeenNotices);
        Assert.Equal(7, result.Public.Condition.Stamina);
        Assert.Equal(blackDiamond, result.Public.HasBlackDiamond);
        Assert.Equal(new CharacterPublicIdentityEntryState(8, 9, 10), result.Public.Identity);
        Assert.Equal(123, result.Public.DerivedStats.PhysicalAttack);
        Assert.Equal(11, result.Public.Extras.ManageLevel);
        Assert.Equal(new CharacterPvpResultStorageRow(101, 102, 103),
            Assert.Single(result.Public.Pvp.Results));
        Assert.Equal([200], result.Public.Extras.SpecialRewardQuestIds);
        // The stored dimension, then Heras' daily count, which the entry always carries.
        Assert.Equal(new long[] { 9, 3900 }, result.Public.Extras.Dimensions.Select(value => (long)value.Key));
        Assert.Equal(4, result.Public.Extras.DimensionFlags.Flag4);
        Assert.Equal([20, 10], result.Settings.HotkeySlots);
        var skill = Assert.Single(Assert.Single(result.Skills.Pages).Skills);
        Assert.Equal((short)4, skill.Slot);
        Assert.Equal((uint)100, skill.SkillId);
        Assert.Equal([9, 8], skill.ExtraValues);
        Assert.Empty(result.Skills.SkillPointSlots);
        Assert.Equal(5, Assert.Single(result.Quests.ActiveQuests).TriggerValue);
        var daily = Assert.Single(result.Progression.DailyChallenges);
        Assert.Equal((uint)900, daily.GroupId);
        Assert.Equal((uint)20, Assert.Single(daily.Entries).ValueB);
        Assert.Equal([1, 2], result.Progression.DailyChallengeTailIds);
        Assert.Equal(-1234, result.Progression.ChampionBreakKey);
        Assert.Equal(new CharacterPremiumUsageSlotState(1, 3),
            Assert.Single(result.Progression.PremiumUsageSlots));
        Assert.Equal((byte)2, Assert.Single(result.Progression.Dungeons).State);
        Assert.Equal(4, Assert.Single(result.Progression.ScheduleCompatibility).Value3);
        var achievement = Assert.Single(result.Features.Collections.Achievements);
        Assert.Equal((ushort)4, achievement.P4);
        var title = Assert.Single(result.Features.Collections.TitleBooks[0].Entries);
        Assert.Equal(1001, title.ItemId);
        Assert.Equal(17, title.Value);
        Assert.Equal(6, result.Features.ExpertJob.EnchanterEndurance);
        Assert.Equal([70], result.Features.ExpertJob.LearnedRecipeIds);
    }

    /// <summary>
    /// Every concept the profile holds reaches the entry state as it was stored.
    /// </summary>
    /// <remarks>
    /// The projection used to be a forty-five-field positional copy, and it is now a pass-through;
    /// what it can get wrong changed with it. A copy could put a value in the wrong field, and the
    /// characterization pinned that. A pass-through can only drop or substitute a whole concept --
    /// and nothing said so: zeroing a stat inside it left the entire suite green, because that
    /// stat's journey from storage to the wire was pinned at neither end of the projection.
    /// </remarks>
    [Fact]
    public void The_profile_projection_hands_every_concept_through_unchanged()
    {
        var storage = Storage(new AccountId(1), new CharacterId(2)).Profile;

        var (entry, settings) = new CharacterProfileEntryProjection().Build(storage);

        Assert.Same(storage.Condition, entry.Condition);
        Assert.Same(storage.Settings, entry.Settings);
        Assert.Same(storage.Pvp, entry.Pvp);
        Assert.Same(storage.ExpertJob, entry.ExpertJob);
        Assert.Same(storage.DerivedStats, entry.DerivedStats);
        Assert.Same(storage.Guild, entry.Guild);
        Assert.Equal(storage.SkillTreeIndex, entry.Extras.SkillTreeIndex);
        Assert.Equal(storage.NameTagItemId, entry.Extras.NameTagItemId);
        Assert.Equal(storage.NameTagExpireTime, entry.Extras.NameTagExpireTime);
        Assert.Equal(storage.ManageLevel, entry.Extras.ManageLevel);
        Assert.Equal(storage.CharacterExperience, entry.Extras.CharacterExperience);
        Assert.Equal(storage.ExtraEquipmentSlotState, entry.Extras.ExtraEquipmentSlotState);
        Assert.Same(storage.Dimensions, entry.Extras.Dimensions);
        Assert.Equal(storage.DimensionFlags, entry.Extras.DimensionFlags);
        Assert.Equal(storage.HotkeySlots, settings.HotkeySlots);
    }

    private static CharacterEntryStorageRecord Storage(AccountId accountId, CharacterId characterId) => new(
        accountId,
        characterId,
        new CharacterProfileStorageRecord(
            1, 60, 44, [1, 2],
            new CharacterConditionStorageRecord(7, 8, 1, 0, 0, 0, 0, 0, 5),
            new CharacterSettingsStorageRecord(3),
            new CharacterPvpStandingStorageRecord(
                99, 0, 0, 0, [new CharacterPvpResultStorageRow(101, 102, 103)]),
            new CharacterExpertJobStorageRecord(2, 10),
            new CharacterDerivedStatsStorageRecord(
                100, 90, 123, 50, 111, 40, 20, 30, 10, 120, 1, 2),
            new CharacterGuildStandingStorageRecord(0, 0),
            SkillTreeIndex: 0, NameTagItemId: 77, NameTagExpireTime: 88, ManageLevel: 11,
            HotkeySlots: [20, 10], CharacterOptionBlob: [4], CharacterExperience: 1000,
            ExtraEquipmentSlotState: 1, Dimensions: [(0, 9L, 1, 2)], DimensionFlags: (1, 2, 3, 4),
            PvpGrade: 8, PvpRatingGrade: 9, UserState: 10),
        new CharacterSkillStorageRecord(
            1, 10, 20,
            [new CharacterSkillStorageRow(0, 4, 100, 5, [9, 8])],
            [], []),
        new CharacterQuestStorageRecord(
            [new CharacterActiveQuestStorageRow(2, 200, 5)], [201], [(200, (byte)1)]),
        new CharacterProgressionStorageRecord(
            60, [2], 1, 255, -1234, 1, 99, 2,
            [new CharacterDailyChallengeStorageGroup(0, 900, [(0, 901, 10, 20)], true)],
            [1, 2], [(0, 1, 2, 3, 4)], [(0, 300, 2)], [(1, 3)], true),
        new CharacterFeatureStorageRecord(
            [new CharacterPetStorageRow(0, 100, 1, 2, 3, 4, 5, 6, [7], 8)],
            [new CharacterTitleBookStorageRow(0, 1, ItemCore(1001, 17))],
            [new CharacterAchievementStorageRow(0, 500, 1, 2, 3, 4)],
            [(3, 0, 700)],
            (2, 10, 1, 3, 4, 6, [70]),
            new CharacterMailboxStorageRecord(0, []),
            [(0, 900, 12)]));

    private static byte[] ItemCore(int itemId, int value)
    {
        var core = new byte[82];
        BitConverter.GetBytes(itemId).CopyTo(core, 1);
        BitConverter.GetBytes(value).CopyTo(core, 5);
        return core;
    }

    private sealed class StubRepository(CharacterEntryStorageRecord record)
        : ICharacterEntryStateRepository
    {
        public ValueTask<CharacterEntryStorageRecord> LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) => ValueTask.FromResult(record);
    }

    /// <summary>Nothing reached through here works out a quest reward.</summary>
    private sealed class NoQuestParameters : IQuestParameterSource
    {
        public QuestParameters Load() =>
            throw new NotSupportedException(
                "No test on this path asks what a quest pays; a value here would be invented.");
    }

    private sealed class StubQuestDefinitions(IReadOnlyCollection<int> specialIds)
        : IQuestDefinitionSource
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([]);

        public ValueTask<QuestDefinition?> GetAsync(int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(specialIds.Contains(questId) ? Definition(questId) : null);

        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> GetTrainingAvailabilityAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);

        private static QuestDefinition Definition(int questId) => new(
            questId, "special", null, null, null, null, null, null, [], "", "",
            null, null, null, null, null, null, null, [], [], [], [], true);
    }
}
