using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Guilds;

[Trait("Category", "Unit")]
public sealed class GuildTravelRuleTests
{
    private static readonly TownAreaDefinition Area = new(1, "town/map", TownAreaKind.Normal, 0, 0)
        { MinimumLevel = 20 };

    [Fact]
    public async Task Outside_entry_requires_a_guild_instance_a_town_gate_and_the_initial_hall_area()
    {
        var town = new TownDefinition(3, "town", [Area, Area with { AreaId = 2, Kind = TownAreaKind.Gate }]);
        var rules = new GuildTravelRuleService(TestRequired.Absent<ITownDefinitionSource>((_, args) =>
            ValueTask.FromResult<TownDefinition?>((int)args![0]! == town.TownId ? town : null)));
        var gate = new CharacterPositionState(3, 2, 0, 0, 0);
        Assert.Null(await rules.RefuseHallEntryAsync(gate, GuildHallTown.InitialAreaId, 5, default));
        Assert.Equal(TownCrossingRefusal.GuildHallEntry, await rules.RefuseHallEntryAsync(gate, 0, null, default));
        Assert.Equal(TownCrossingRefusal.GuildHallEntry, await rules.RefuseHallEntryAsync(gate, 1, 5, default));
        Assert.Equal(TownCrossingRefusal.GuildHallEntry, await rules.RefuseHallEntryAsync(gate with { AreaId = 1 }, 0, 5, default));
    }

    [Theory]
    [InlineData(0, 1, true, false, true)]
    [InlineData(1, 0, true, false, true)]
    [InlineData(1, 1, false, true, false)]
    [InlineData(0, 2, true, false, false)]
    [InlineData(0, 2, true, true, true)]
    [InlineData(2, 0, true, true, true)]
    [InlineData(2, 2, true, true, true)]
    [InlineData(1, 2, true, true, false)]
    [InlineData(0, 2, false, true, false)]
    public async Task Inside_crossings_require_a_current_visit_and_a_valid_area_pair(
        byte source, byte destination, bool hasVisit, bool mapLinksBattleArea, bool allowed)
    {
        var hall = new TownDefinition(GuildHallTown.TownId, "hall", [Area with { AreaId = GuildHallTown.InitialAreaId }]);
        var map = new MapGeometry(mapLinksBattleArea ? [new(default, hall.TownId, GuildHallTown.BattleAreaId)] : [], []);
        var rules = new GuildTravelRuleService(TestRequired.Absent<ITownDefinitionSource>((method, args) => method.Name switch
        {
            nameof(ITownDefinitionSource.GetAsync) => ValueTask.FromResult<TownDefinition?>((int)args![0]! == hall.TownId ? hall : null),
            nameof(ITownDefinitionSource.GetMapGeometryAsync) => ValueTask.FromResult<MapGeometry?>((string)args![0]! == Area.MapPath ? map : null),
            _ => throw new InvalidOperationException(method.Name),
        }));
        var refusal = await rules.RefuseHallCrossingAsync(source, destination, hasVisit, default);
        Assert.Equal(allowed ? (TownCrossingRefusal?)null : TownCrossingRefusal.GuildHallEntry, refusal);
    }

    [Fact]
    public void Leaving_the_hall_requires_the_stored_home_town_and_area()
    {
        var home = new CharacterPositionState(3, 1, 0, 0, 0);
        Assert.Null(GuildTravelRuleService.RefuseHallExit(home, 3, 1));
        Assert.Equal(TownCrossingRefusal.GuildHallExit, GuildTravelRuleService.RefuseHallExit(null, 3, 1));
        Assert.Equal(TownCrossingRefusal.GuildHallExit, GuildTravelRuleService.RefuseHallExit(home, 3, 2));
        Assert.Equal(TownCrossingRefusal.GuildHallExit, GuildTravelRuleService.RefuseHallExit(home, 4, 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(19)]
    public void Missing_or_insufficient_level_locks_the_destination_before_loading_quests(int? level)
    {
        var gated = Area with { RequiredQuestIds = [10] };
        Assert.Equal(GuildTeleportEligibility.Locked, GuildTravelRuleService.EvaluateTeleportEligibility(gated, level));
    }

    [Fact]
    public void At_the_minimum_level_only_quest_gated_destinations_need_the_log()
    {
        Assert.Equal(GuildTeleportEligibility.Allowed, GuildTravelRuleService.EvaluateTeleportEligibility(Area, 20));
        var gated = Area with { RequiredQuestIds = [10, 11] };
        Assert.Equal(GuildTeleportEligibility.NeedsQuestLog, GuildTravelRuleService.EvaluateTeleportEligibility(gated, 20));
        GuildTeleportEligibility Evaluate(params ClearedQuest[] cleared) =>
            GuildTravelRuleService.EvaluateTeleportEligibility(gated, 20, cleared);

        Assert.Equal(GuildTeleportEligibility.Locked, Evaluate());
        Assert.Equal(GuildTeleportEligibility.Locked, Evaluate(new(10, 1), new(12, 1)));
        Assert.Equal(GuildTeleportEligibility.Locked, Evaluate(new(10, 1), new(11, 0)));
        Assert.Equal(GuildTeleportEligibility.Allowed, Evaluate(new(10, 1), new(11, 2)));
    }
}
