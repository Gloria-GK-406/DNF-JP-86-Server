using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class CharacterEntryStateTests
{
    [Fact]
    public async Task CharacterEntryState_DerivesCompleteTypedViewsFromRawSliceStorage()
    {
        var accountId = new AccountId(61);
        var characterId = new CharacterId(71);
        var service = new CharacterEntryStateService(
            new StubRepository(Storage(accountId, characterId)),
            new CharacterProfileEntryService(),
            new CharacterSkillQuestEntryService(),
            new CharacterProgressionEntryService(),
            new CharacterFeatureEntryService());

        var result = await service.LoadAsync(accountId, characterId, CancellationToken.None);

        Assert.Equal((byte)7, result.Public.Subtype0.Stamina);
        Assert.Equal(123, result.Public.Subtype1.PhysicalAttack);
        Assert.Equal((uint)9, Assert.Single(result.Public.Subtype1.Dimensions!).Key);
        Assert.Equal((byte)4, result.Public.Subtype1.DimensionFlags!.Flag4);
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

    private static CharacterEntryStorageRecord Storage(AccountId accountId, CharacterId characterId) => new(
        accountId,
        characterId,
        new CharacterProfileStorageRecord(
            1, 60, 44, [1, 2],
            new CharacterSubtype0StorageRecord(7, 8, 1, 0, 2, 10, 0, 0, 0, 3, 0, 5, 99),
            new CharacterSubtype1StorageRecord(100, 90, 123, 50, 111, 40, 20, 30, 10, 120, 0, 1, 2),
            [20, 10], [4], 1000, 1, [(0, 9L, 1, 2)], (1, 2, 3, 4)),
        new CharacterSkillStorageRecord(
            1, 10, 20,
            [new CharacterSkillStorageRow(0, 4, 100, 5, [9, 8])],
            [], []),
        new CharacterQuestStorageRecord(
            [new CharacterActiveQuestStorageRow(2, 200, 5)], [201], [(200, (byte)1)]),
        new CharacterProgressionStorageRecord(
            60, [2], 1, 255, -1234, 1, 99, 2,
            [new CharacterDailyChallengeStorageGroup(0, 900, [(0, 901, 10, 20)], true)],
            [1, 2], [(0, 1, 2, 3, 4)], [(0, 300, 2)], 3, true),
        new CharacterFeatureStorageRecord(
            [new CharacterPetStorageRow(0, 100, 1, 2, 3, 4, 5, 6, [7], 8)],
            [new CharacterTitleBookStorageRow(0, 1, ItemCore(1001, 17))],
            [new CharacterAchievementStorageRow(0, 500, 1, 2, 3, 4)],
            [(3, 0, 700)],
            (2, 10, 1, 3, 4, 6, [70]),
            new CharacterMailboxStorageRecord(1, [800L]),
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
}
