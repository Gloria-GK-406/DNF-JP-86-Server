using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.World;

[Trait("Category", "Unit")]
public sealed class DungeonDifficultyRuleServiceTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(3, 1, 1)]
    [InlineData(2, 3, 1)] // supported 1 and 3 are equally close; prefer the lower tier
    [InlineData(4, 3, 3)]
    [InlineData(255, 3, -1)]
    [InlineData(0, 255, -1)]
    public async Task Nearest_cycle_tier_honors_supported_tiers_and_unlocks_even_when_story_mode_exists(
        byte requested, byte maximum, int expected)
    {
        var rules = new DungeonDifficultyRuleService(TestRequired.Admissions(new Definitions("dungeon/timegate/a.dgn", story: true)));
        var actual = await rules.NearestUnlockedAsync(70, (DungeonDifficulty)requested, (DungeonDifficulty)maximum, default);
        Assert.Equal(expected < 0 ? null : (DungeonDifficulty?)expected, actual);
        Assert.False(await rules.IsUnlockedAsync(70, DungeonDifficulty.Tier3, DungeonDifficulty.Normal, default));
        Assert.True(await rules.AllowsAsync(70, DungeonDifficulty.Tier3, DungeonDifficulty.Normal, default));
    }

    [Fact]
    public async Task Lowest_declared_tier_is_available_when_normal_is_not_supported()
    {
        var rules = new DungeonDifficultyRuleService(TestRequired.Admissions(new FixedDefinitions()));
        Assert.Equal(DungeonDifficulty.Tier2,
            await rules.NearestUnlockedAsync(1500, DungeonDifficulty.Normal, DungeonDifficulty.Normal, default));
    }
    [Theory]
    [InlineData("dungeon/timegate/a.dgn", 0, 1)]
    [InlineData("dungeon/timegate/a.dgn", 1, 3)]
    [InlineData("dungeon/timegate/a.dgn", 3, -1)]
    [InlineData("dungeon/timegate/a.dgn", 2, -1)]
    [InlineData("dungeon/quest/a.dgn", 0, -1)]
    public async Task Clear_uses_declared_tiers_and_does_not_advance_quest_dungeons(string path, byte cleared, int next)
    {
        var rules = new DungeonDifficultyRuleService(TestRequired.Admissions(new Definitions(path)));
        var result = await rules.NextAfterClearAsync(70, (DungeonDifficulty)cleared, default);
        Assert.Equal(next < 0 ? null : (DungeonDifficulty?)next, result);
    }

    [Theory]
    [InlineData(2, 0, true)]
    [InlineData(0, 4, false)]
    [InlineData(3, 4, false)]
    public async Task Fixed_ancient_difficulty_is_available_without_progression(byte requested, byte maximum, bool expected)
    {
        var rules = new DungeonDifficultyRuleService(TestRequired.Admissions(new FixedDefinitions()));
        Assert.Equal(expected, await rules.AllowsAsync(1500, (DungeonDifficulty)requested, (DungeonDifficulty)maximum, default));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, true)]
    [InlineData(2, 4, false)]
    public async Task Ordinary_tiers_still_require_clear_progression(byte requested, byte maximum, bool expected)
    {
        var rules = new DungeonDifficultyRuleService(TestRequired.Admissions(new Definitions("dungeon/timegate/a.dgn")));
        Assert.Equal(expected, await rules.AllowsAsync(70, (DungeonDifficulty)requested, (DungeonDifficulty)maximum, default));
    }

    private sealed class FixedDefinitions : IDungeonDefinitionSource
    {
        public ValueTask<DungeonDefinition?> GetAsync(int id, CancellationToken token) =>
            ValueTask.FromResult<DungeonDefinition?>(new(id, "dungeon/Ancient/N_Vilmark.dgn", 85, true, [], SupportedDifficulties: [2]));
    }

    private sealed class Definitions(string path, bool story = false) : IDungeonDefinitionSource
    {
        public ValueTask<DungeonDefinition?> GetAsync(int id, CancellationToken token) =>
            ValueTask.FromResult<DungeonDefinition?>(new(id, path, 1, true, [], SupportedDifficulties: [0,1,3]) { HasStoryMode = story });
    }
}
