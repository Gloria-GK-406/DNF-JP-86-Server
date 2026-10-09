using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfHellDungeonDefinitionSourceTests
{
    [SkippableTheory]
    [InlineData(77, 4391, 3330)]
    [InlineData(190, 2002, 3330)]
    public async Task Supported_areas_expose_quests_tickets_and_valid_room_groups(int id, int quest, int ticket)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfHellDungeonDefinitionSource(provider);
        var rules = await source.GetAsync(id, default);
        Assert.NotNull(rules);
        Assert.True(rules.Enabled);
        Assert.Contains(quest, rules.RequiredQuestIds);
        Assert.Contains(ticket, rules.TicketIds);
        Assert.NotEmpty(rules.Rooms);
        var party = await source.GetPartyAsync(default);
        Assert.Contains(party.ManualDifficulties, x => x.Weight > 0);
        foreach (var room in rules.Rooms.Values)
        {
            Assert.NotEmpty(room.Groups);
            Assert.All(room.Groups, x => Assert.Contains(party.Groups, group => group.Index == x.GroupIndex));
            Assert.NotNull(await new PvfDungeonMapDefinitionSource(provider).GetAsync(room.MapId, default));
        }
    }

    [SkippableFact]
    public async Task Tutorial_has_no_manual_hell_and_watchtower_uses_declared_seal_room()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfHellDungeonDefinitionSource(provider);
        Assert.Null(await source.GetAsync(10000, default));
        var rules = await source.GetAsync(190, default);
        Assert.NotNull(rules);
        Assert.Equal(60078, rules.Rooms[0].MapId);
        Assert.Equal(new MazeCell(3, 3), rules.Rooms[0].Cell);
    }
}
