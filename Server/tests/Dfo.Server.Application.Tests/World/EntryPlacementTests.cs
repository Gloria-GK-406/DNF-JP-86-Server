using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Tests.World;

public sealed class EntryPlacementTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(7, false)]
    [InlineData(17, false)]
    [InlineData(999, false)]
    public async Task Only_ordinary_towns_with_a_usable_gate_can_be_remembered_as_an_inn(int town, bool expected)
    {
        Assert.Equal(expected, await Placement().CanRememberInnAsync(town, default));
    }

    [Theory]
    [InlineData(ChannelEntryKind.FreeDuel, 10)]
    [InlineData(ChannelEntryKind.Pvp, 10)]
    [InlineData(ChannelEntryKind.Raid, 19)]
    public async Task Special_channel_uses_its_own_town_without_overwriting_home(ChannelEntryKind kind, int town)
    {
        var placement = Placement();
        var channel = new ChannelEntryIdentity(68, 10068, kind);
        var result = await placement.ResolveAsync(channel, 2, default);
        Assert.Equal(town, result.Spawn.TownId);
        Assert.True(result.Spawn.IsTransient);
        Assert.False(result.PersistsPosition);
        Assert.True(await placement.CanEnterTownAsync(channel, town, default));
        Assert.False(await placement.CanEnterTownAsync(channel, 2, default));
        Assert.False(await placement.CanEnterTownAsync(StubChannelEntryPolicy.GeneralChannel, town, default));
        Assert.False(await placement.CanRememberInnAsync(town, default));
        var home = await placement.ResolveAsync(StubChannelEntryPolicy.GeneralChannel, 2, default);
        Assert.Equal(2, home.Spawn.TownId);
        var historicalSpecialSave = await placement.ResolveAsync(StubChannelEntryPolicy.GeneralChannel, town, default);
        Assert.Equal(1, historicalSpecialSave.Spawn.TownId);
    }

    [Fact]
    public async Task A_character_arrives_at_the_gate_of_the_town_they_were_last_in()
    {
        var placement = Placement();

        var result = await placement.ResolveAsync(
            StubChannelEntryPolicy.GeneralChannel,
            persistedTownId: 2,
            CancellationToken.None);

        Assert.Equal(2, result.Spawn.TownId);
        Assert.Equal(0, result.Spawn.AreaId);
        Assert.Equal(300, result.Spawn.X);
        Assert.Equal(400, result.Spawn.Y);
        Assert.False(result.Spawn.IsTransient);
        Assert.False(result.Spawn.SharesTownPresence);
    }

    [Fact]
    public async Task A_character_with_no_remembered_town_arrives_at_the_default_one()
    {
        var placement = Placement();

        var result = await placement.ResolveAsync(
            StubChannelEntryPolicy.GeneralChannel,
            persistedTownId: 0,
            CancellationToken.None);

        Assert.Equal(1, result.Spawn.TownId);
        Assert.Equal(474, result.Spawn.X);
        Assert.Equal(234, result.Spawn.Y);
    }

    [Fact]
    public async Task An_ordinary_channel_persists_the_resolved_position()
    {
        var result = await Placement().ResolveAsync(
            StubChannelEntryPolicy.GeneralChannel,
            persistedTownId: 1,
            CancellationToken.None);

        Assert.True(result.PersistsPosition);
        Assert.False(result.Spawn.IsTransient);
    }

    /// <remarks>
    /// Writing a transient position back would strand the character on the restricted
    /// channel's town the next time they log in anywhere.
    /// </remarks>
    [Fact]
    public async Task The_restricted_channel_places_transiently_and_does_not_persist()
    {
        var result = await Placement().ResolveAsync(
            StubChannelEntryPolicy.RestrictedChannel,
            persistedTownId: 2,
            CancellationToken.None);

        Assert.True(result.Spawn.IsTransient);
        Assert.False(result.PersistsPosition);
        Assert.Equal(17, result.Spawn.TownId);
        Assert.Equal(0, result.Spawn.AreaId);
        Assert.Equal(474, result.Spawn.X);
        Assert.Equal(234, result.Spawn.Y);
        Assert.False(result.Spawn.SharesTownPresence);
    }

    [Fact]
    public async Task The_duelling_profile_applies_only_on_the_duelling_channel()
    {
        var placement = Placement();

        var duel = await placement.ResolveAsync(
            StubChannelEntryPolicy.FreeDuelChannel, 1, CancellationToken.None);
        var general = await placement.ResolveAsync(
            StubChannelEntryPolicy.GeneralChannel, 1, CancellationToken.None);

        Assert.True(duel.PvpSkillProfileApplies);
        Assert.False(general.PvpSkillProfileApplies);
    }

    [Fact]
    public async Task The_restricted_channel_admits_only_its_own_town()
    {
        var placement = Placement();

        Assert.True(await placement.CanEnterTownAsync(
            StubChannelEntryPolicy.RestrictedChannel, 17, CancellationToken.None));
        Assert.False(await placement.CanEnterTownAsync(
            StubChannelEntryPolicy.RestrictedChannel, 1, CancellationToken.None));
        Assert.True(await placement.CanEnterTownAsync(
            StubChannelEntryPolicy.GeneralChannel, 1, CancellationToken.None));
        Assert.False(await placement.CanEnterTownAsync(
            StubChannelEntryPolicy.GeneralChannel, 17, CancellationToken.None));
    }

    [Fact]
    public async Task A_gate_is_private_while_an_ordinary_town_area_is_shared()
    {
        var placement = Placement();

        Assert.False(await placement.SharesTownPresenceAsync(1, 0, CancellationToken.None));
        Assert.True(await placement.SharesTownPresenceAsync(1, 1, CancellationToken.None));
        Assert.False(await placement.SharesTownPresenceAsync(1, 99, CancellationToken.None));
    }

    [Theory]
    [InlineData(999)]
    [InlineData(7)]
    public async Task A_missing_town_or_gate_falls_back_to_the_initial_town(int townId)
    {
        var result = await Placement().ResolveAsync(
            StubChannelEntryPolicy.GeneralChannel, townId, CancellationToken.None);
        Assert.Equal(1, result.Spawn.TownId);
        Assert.Equal(0, result.Spawn.AreaId);
        Assert.Equal(474, result.Spawn.X);
        Assert.Equal(234, result.Spawn.Y);
        Assert.True(result.PersistsPosition);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    public async Task A_missing_initial_gate_still_reports_a_configuration_error(int townId)
    {
        var placement = new EntryPlacementRuleService(
            new StubTownSource([Town(1, [Area(0, "initial.map", TownAreaKind.Normal)])],
                new Dictionary<string, MapGeometry>()), ChannelEntryOptions.Legacy);
        var error = await Assert.ThrowsAsync<EntryPlacementUnavailableException>(async () =>
            await placement.ResolveAsync(StubChannelEntryPolicy.GeneralChannel, townId, CancellationToken.None));
        Assert.Contains("Initial town 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invalid_arrival_coordinate_falls_back_to_the_initial_town()
    {
        var placement = new EntryPlacementRuleService(new StubTownSource(
            [Town(1, [Area(0, "initial.map", TownAreaKind.Gate, 474, 234)]),
             Town(2, [Area(0, "invalid.map", TownAreaKind.Gate, int.MaxValue, 234)])],
            new Dictionary<string, MapGeometry>()), ChannelEntryOptions.Legacy);
        var result = await placement.ResolveAsync(StubChannelEntryPolicy.GeneralChannel, 2, CancellationToken.None);
        Assert.Equal(1, result.Spawn.TownId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saint_horn_requires_its_own_valid_gate_and_never_falls_back_to_an_ordinary_town(bool invalidCoordinates)
    {
        var specialArea = invalidCoordinates
            ? Area(0, "special.map", TownAreaKind.Gate, int.MaxValue, 234)
            : Area(4, "old-exit.map", TownAreaKind.DungeonGate);
        var placement = new EntryPlacementRuleService(new StubTownSource(
            [Town(1, [Area(0, "initial.map", TownAreaKind.Gate, 474, 234)]),
             Town(17, [specialArea])], new Dictionary<string, MapGeometry>()), ChannelEntryOptions.Legacy);
        await Assert.ThrowsAsync<EntryPlacementUnavailableException>(async () =>
            await placement.ResolveAsync(StubChannelEntryPolicy.RestrictedChannel, 1, default));
    }

    [Fact]
    public async Task Returning_to_an_ordinary_channel_recovers_the_saved_home_gate()
    {
        var placement = Placement();
        var boat = await placement.ResolveAsync(StubChannelEntryPolicy.RestrictedChannel, 2, default);
        Assert.False(boat.PersistsPosition);
        var home = await placement.ResolveAsync(StubChannelEntryPolicy.GeneralChannel, 2, default);
        Assert.Equal(2, home.Spawn.TownId);
        Assert.Equal(300, home.Spawn.X);
        Assert.True(home.PersistsPosition);
    }

    [Fact]
    public async Task A_historical_special_town_save_cannot_put_a_general_channel_on_the_boat()
    {
        var result = await Placement().ResolveAsync(StubChannelEntryPolicy.GeneralChannel, 17, default);
        Assert.Equal(1, result.Spawn.TownId);
    }

    private static EntryPlacementRuleService Placement() =>
        new(new StubTownSource(
            [
                Town(1,
                [
                    Area(0, "silver-gate.map", TownAreaKind.Gate, 474, 234),
                    Area(1, "silver-street.map", TownAreaKind.Normal),
                ]),
                Town(2, [Area(0, "second.map", TownAreaKind.Gate, 300, 400)]),
                // Town 7 has areas but no gate, which the real content also contains.
                Town(7, [Area(0, "warfare.map", TownAreaKind.Unrecognized)]),
                Town(17, [Area(0, "saint-gate.map", TownAreaKind.Gate, 474, 234)]),
                Town(10, [Area(0, "pvp-gate.map", TownAreaKind.Gate, 300, 200)]),
                Town(19, [Area(0, "raid-gate.map", TownAreaKind.Gate, 400, 200)]),
            ], new Dictionary<string, MapGeometry>()),
            ChannelEntryOptions.Legacy);

    private static TownDefinition Town(int townId, IReadOnlyList<TownAreaDefinition> areas) =>
        new(townId, $"town-{townId}", areas);

    private static TownAreaDefinition Area(
        int areaId,
        string mapPath,
        TownAreaKind kind,
        int operand1 = TownAreaDefinition.NoOperand,
        int operand2 = TownAreaDefinition.NoOperand) =>
        new(areaId, mapPath, kind, operand1, operand2);

    private sealed class StubTownSource(
        IReadOnlyList<TownDefinition> towns,
        IReadOnlyDictionary<string, MapGeometry> geometry) : ITownDefinitionSource
    {
        public ValueTask<IReadOnlyList<TownDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(towns);

        public ValueTask<TownDefinition?> GetAsync(int townId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(towns.FirstOrDefault(town => town.TownId == townId));

        public ValueTask<MapGeometry?> GetMapGeometryAsync(
            string mapPath,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(geometry.GetValueOrDefault(mapPath));
    }
}
