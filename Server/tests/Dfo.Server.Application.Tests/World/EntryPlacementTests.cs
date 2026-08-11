using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Tests.World;

public sealed class EntryPlacementTests
{
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
        Assert.Equal(4, result.Spawn.AreaId);
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
    }

    [Fact]
    public async Task A_town_the_content_does_not_define_fails_and_names_it()
    {
        var error = await Assert.ThrowsAsync<EntryPlacementUnavailableException>(async () =>
            await Placement().ResolveAsync(
                StubChannelEntryPolicy.GeneralChannel,
                persistedTownId: 999,
                CancellationToken.None));

        Assert.Contains("999", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_town_that_declares_no_gate_fails_and_names_it()
    {
        var error = await Assert.ThrowsAsync<EntryPlacementUnavailableException>(async () =>
            await Placement().ResolveAsync(
                StubChannelEntryPolicy.GeneralChannel,
                persistedTownId: 7,
                CancellationToken.None));

        Assert.Contains("7", error.Message, StringComparison.Ordinal);
        Assert.Contains("gate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <remarks>
    /// The exit rectangle is 30 wide and 120 tall at (340, 218), so its centre is (355, 278)
    /// and the four candidates are (332, 278), (378, 278), (355, 210) and (355, 346). Only
    /// the second lies inside a walkable rectangle, so it is the arrival point. Getting the
    /// clearance or the side wrong would still yield a plausible coordinate, which is why
    /// the expected point is stated rather than merely range-checked.
    /// </remarks>
    [Fact]
    public async Task The_transient_arrival_is_the_nearest_walkable_point_beside_the_local_exit()
    {
        var result = await Placement().ResolveAsync(
            StubChannelEntryPolicy.RestrictedChannel,
            persistedTownId: 1,
            CancellationToken.None);

        Assert.Equal(378, result.Spawn.X);
        Assert.Equal(278, result.Spawn.Y);
    }

    /// <remarks>
    /// Exits that lead to another town map are not arrival points. Considering them would
    /// place the character beside a door to somewhere else.
    /// </remarks>
    [Fact]
    public async Task An_exit_that_leads_to_another_map_is_not_used_as_an_arrival_point()
    {
        var towns = new StubTownSource(
            [Town(17, [Area(4, "restricted.map", TownAreaKind.DungeonGate)])],
            new Dictionary<string, MapGeometry>
            {
                // The only exit here links to town 3, so nothing qualifies.
                ["restricted.map"] = new MapGeometry(
                    [new TownTransitArea(new MapRectangle(100, 100, 10, 10), 3, 1)],
                    [new MapRectangle(0, 0, 1000, 1000)]),
            });

        var error = await Assert.ThrowsAsync<EntryPlacementUnavailableException>(async () =>
            await new EntryPlacementService(towns, ChannelEntryOptions.Legacy).ResolveAsync(
                StubChannelEntryPolicy.RestrictedChannel,
                persistedTownId: 1,
                CancellationToken.None));

        Assert.Contains("arrival", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <remarks>
    /// The arrival is computed from a local exit's geometry, which only an area leading out
    /// of town has. Reading an ordinary area's map would still produce a coordinate, and
    /// nothing downstream would question it, so the kind is checked rather than assumed.
    /// </remarks>
    [Fact]
    public async Task An_area_that_does_not_lead_out_of_town_is_not_an_arrival_point()
    {
        var towns = new StubTownSource(
            [Town(17, [Area(4, "restricted.map", TownAreaKind.Normal)])],
            new Dictionary<string, MapGeometry>
            {
                ["restricted.map"] = new MapGeometry(
                    [new TownTransitArea(new MapRectangle(340, 218, 30, 120), -1, -1)],
                    [new MapRectangle(0, 0, 1000, 1000)]),
            });

        var error = await Assert.ThrowsAsync<EntryPlacementUnavailableException>(async () =>
            await new EntryPlacementService(towns, ChannelEntryOptions.Legacy).ResolveAsync(
                StubChannelEntryPolicy.RestrictedChannel,
                persistedTownId: 1,
                CancellationToken.None));

        Assert.Contains("Normal", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_exit_with_no_walkable_neighbour_yields_no_arrival_point()
    {
        var towns = new StubTownSource(
            [Town(17, [Area(4, "restricted.map", TownAreaKind.DungeonGate)])],
            new Dictionary<string, MapGeometry>
            {
                ["restricted.map"] = new MapGeometry(
                    [new TownTransitArea(new MapRectangle(100, 100, 10, 10), -1, -1)],
                    []),
            });

        await Assert.ThrowsAsync<EntryPlacementUnavailableException>(async () =>
            await new EntryPlacementService(towns, ChannelEntryOptions.Legacy).ResolveAsync(
                StubChannelEntryPolicy.RestrictedChannel,
                persistedTownId: 1,
                CancellationToken.None));
    }

    private static EntryPlacementService Placement() =>
        new(new StubTownSource(
            [
                Town(1, [Area(0, "silver.map", TownAreaKind.Gate, 474, 234)]),
                Town(2, [Area(0, "second.map", TownAreaKind.Gate, 300, 400)]),
                // Town 7 has areas but no gate, which the real content also contains.
                Town(7, [Area(0, "warfare.map", TownAreaKind.Unrecognized)]),
                Town(17, [Area(4, "restricted.map", TownAreaKind.DungeonGate)]),
            ],
            new Dictionary<string, MapGeometry>
            {
                // Reproduces the shape the real restricted-channel map declares.
                ["restricted.map"] = new MapGeometry(
                    [
                        new TownTransitArea(new MapRectangle(1323, 157, 30, 258), 17, 1),
                        new TownTransitArea(new MapRectangle(340, 218, 30, 120), -1, -1),
                    ],
                    [
                        new MapRectangle(617, 177, 750, 230),
                        new MapRectangle(346, 228, 280, 100),
                        new MapRectangle(555, 307, 280, 100),
                    ]),
            }),
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
