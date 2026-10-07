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

    private sealed class Definitions(string path) : IDungeonDefinitionSource
    {
        public ValueTask<DungeonDefinition?> GetAsync(int id, CancellationToken token) =>
            ValueTask.FromResult<DungeonDefinition?>(new(id, path, 1, true, [], SupportedDifficulties: [0,1,3]));
    }
}
