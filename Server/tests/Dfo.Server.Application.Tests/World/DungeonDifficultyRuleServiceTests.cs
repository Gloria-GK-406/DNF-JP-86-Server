using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonDifficultyRuleServiceTests
{
    [Theory]
    [InlineData("dungeon/timegate/a.dgn", 0, 1)]
    [InlineData("dungeon/timegate/a.dgn", 1, 3)]
    [InlineData("dungeon/timegate/a.dgn", 3, -1)]
    [InlineData("dungeon/timegate/a.dgn", 2, -1)]
    [InlineData("dungeon/quest/a.dgn", 0, -1)]
    public async Task Clear_uses_declared_tiers_and_does_not_advance_quest_dungeons(string path, byte cleared, int next)
    {
        var rules = new DungeonDifficultyRuleService(new Definitions(path));
        var result = await rules.NextAfterClearAsync(70, (DungeonDifficulty)cleared, default);
        Assert.Equal(next < 0 ? null : (DungeonDifficulty?)next, result);
    }

    [Theory]
    [InlineData(2, 0, true)]
    [InlineData(0, 4, false)]
    [InlineData(3, 4, false)]
    public async Task Fixed_ancient_difficulty_is_available_without_progression(byte requested, byte maximum, bool expected)
    {
        var rules = new DungeonDifficultyRuleService(new FixedDefinitions());
        Assert.Equal(expected, await rules.AllowsAsync(1500, (DungeonDifficulty)requested, (DungeonDifficulty)maximum, default));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, true)]
    [InlineData(2, 4, false)]
    public async Task Ordinary_tiers_still_require_clear_progression(byte requested, byte maximum, bool expected)
    {
        var rules = new DungeonDifficultyRuleService(new Definitions("dungeon/timegate/a.dgn"));
        Assert.Equal(expected, await rules.AllowsAsync(70, (DungeonDifficulty)requested, (DungeonDifficulty)maximum, default));
    }

    private sealed class FixedDefinitions : IDungeonDefinitionSource
    {
        public ValueTask<DungeonDefinition?> GetAsync(int id, CancellationToken token) =>
            ValueTask.FromResult<DungeonDefinition?>(new(id, "dungeon/Ancient/N_Vilmark.dgn", 85, true, [], SupportedDifficulties: [2]));
    }

    private sealed class Definitions(string path) : IDungeonDefinitionSource
    {
        public ValueTask<DungeonDefinition?> GetAsync(int id, CancellationToken token) =>
            ValueTask.FromResult<DungeonDefinition?>(new(id, path, 1, true, [], SupportedDifficulties: [0,1,3]));
    }
}
