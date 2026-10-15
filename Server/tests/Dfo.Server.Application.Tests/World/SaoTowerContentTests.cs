using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class SaoTowerContentTests
{
    [SkippableFact]
    public async Task All_hundred_floors_resolve_with_start_room_actors_and_native_ticket_costs()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        var catalog = content.GetRequiredService<ISaoTowerContentSource>().Load();
        Assert.Equal(100, catalog.Floors.Count);
        Assert.Equal(10004331, catalog.FirstClearRewardItemId);
        foreach (var floor in catalog.Floors)
        {
            Assert.Equal(3599 + floor.Floor, floor.DungeonId);
            Assert.Equal(39, floor.MinimumLevel);
            Assert.Equal(10004322, Assert.Single(floor.Tickets).ItemId);
        }
        await using var f = new Dfo.Server.Application.Tests.Characters.LearningFixture(useArchive: true,
            configure: s => s.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path }));
        var layouts = f.Services.GetRequiredService<IDungeonStructureRuleService>();
        var actors = content.GetRequiredService<Dfo.Server.Abstractions.Content.IDungeonRoomActorSource>();
        var errors = new List<string>();
        foreach (var floor in catalog.Floors)
        {
            try
            {
                var layout = await layouts.ResolveAsync(floor.DungeonId, DungeonEntryFacts.None, 13, default);
                Assert.NotNull(layout);
                Assert.Equal(layout.StartCell, layout.BossCell);
                var room = await actors.GetAsync(layout.StartRoomMapId, default);
                Assert.NotEmpty(room);
            }
            catch (InvalidDataException error) { errors.Add($"Floor {floor.Floor}: {error.Message}"); }
        }
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }
}
