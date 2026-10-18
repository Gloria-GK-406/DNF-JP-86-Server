using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class HerasDropTests
{
    [SkippableTheory]
    [InlineData(64923, 2)]
    [InlineData(64930, 1)]
    [InlineData(64931, 1)]
    [InlineData(64932, 1)]
    [InlineData(110920, 1)]
    [InlineData(110921, 1)]
    public async Task Current_archive_gives_guaranteed_parts_and_a_separate_optional_part(int monster, int guaranteed)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = new LearningFixture(useArchive: true, configure: s => s.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path }));
        await f.SeedAsync();
        var drops = f.Services.GetRequiredService<IIndependentDropRuleService>();
        drops.Initialize();
        var optional = 0;
        for (var seed = 1; seed <= 100; seed++)
        {
            var context = new MonsterDropContext(DungeonRunId.New(), 3900, seed, new(1, 1), 27116,
                (DungeonDifficulty)1, 1, 85, 3, new(71), [new(new(1), new(71))]);
            var items = await drops.DrawAsync(new(monster, context), default);
            var count = items.Where(x => x.ItemId == 490002846).Sum(x => x.Count);
            Assert.InRange(count, guaranteed, guaranteed + 1);
            optional += count - guaranteed;
            Assert.DoesNotContain(await drops.DrawAsync(new(monster, context with { SourceType = IndependentDropSourceType.APC }), default), x => x.ItemId == 490002846);
        }
        Assert.InRange(optional, 30, 85);
    }
}
