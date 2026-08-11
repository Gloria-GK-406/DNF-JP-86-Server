using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfTownDefinitionSourceTests
{
    [SkippableFact]
    public async Task The_town_list_resolves_to_town_files()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfTownDefinitionSource(provider);

        var towns = await source.GetAllAsync(CancellationToken.None);

        Assert.NotEmpty(towns);
        Assert.All(towns, town => Assert.NotEmpty(town.Areas));
        Assert.Equal(towns.Select(town => town.TownId).Distinct().Count(), towns.Count);
    }

    [SkippableFact]
    public async Task A_town_states_the_area_a_character_arrives_in_and_where_they_stand()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfTownDefinitionSource(provider);

        var town = await source.GetAsync(1, CancellationToken.None);

        Assert.NotNull(town);
        var gate = town!.Gate;
        Assert.NotNull(gate);
        Assert.Equal(0, gate!.AreaId);
        Assert.Equal(474, gate.Operand1);
        Assert.Equal(234, gate.Operand2);
        Assert.NotEmpty(gate.MapPath);
    }

    /// <remarks>
    /// The area kind arrives as a quoted value rather than a section tag, so a reader that
    /// only matched identifiers would classify every area as unrecognized while still
    /// producing a plausible-looking town. The content also declares kinds this contract
    /// does not model, such as the PvP safe zones, so unrecognized areas legitimately
    /// exist; what must not happen is that the three entry-relevant kinds go missing.
    /// </remarks>
    [SkippableFact]
    public async Task The_three_entry_relevant_area_kinds_are_all_classified()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfTownDefinitionSource(provider);

        var areas = (await source.GetAllAsync(CancellationToken.None))
            .SelectMany(town => town.Areas)
            .ToList();

        Assert.Contains(areas, area => area.Kind is TownAreaKind.Gate);
        Assert.Contains(areas, area => area.Kind is TownAreaKind.Normal);
        Assert.Contains(areas, area => area.Kind is TownAreaKind.DungeonGate);
    }

    /// <remarks>
    /// A minimap rectangle is four integers of presentation data that sit between the map
    /// path and the area kind. Counting them as operands yields a spawn at a rectangle
    /// corner, which is wrong in a way nothing downstream would flag, so every gate is
    /// checked for a position the content could plausibly have stated.
    /// </remarks>
    [SkippableFact]
    public async Task A_minimap_rectangle_is_not_mistaken_for_the_area_operands()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfTownDefinitionSource(provider);

        var gates = (await source.GetAllAsync(CancellationToken.None))
            .Select(town => town.Gate)
            .Where(gate => gate is not null)
            .ToList();

        Assert.NotEmpty(gates);
        Assert.All(gates, gate =>
        {
            Assert.True(gate!.Operand1 > 0, $"gate on {gate.MapPath} states no spawn X");
            Assert.True(gate.Operand2 > 0, $"gate on {gate.MapPath} states no spawn Y");
        });
    }

    /// <remarks>
    /// Not every town declares a gate. Entry into one of those cannot resolve a spawn, and
    /// the rule that resolves spawns must fail rather than invent a position, so the source
    /// has to report the absence rather than substitute a default.
    /// </remarks>
    [SkippableFact]
    public async Task A_town_that_declares_no_gate_reports_none()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfTownDefinitionSource(provider);

        var towns = await source.GetAllAsync(CancellationToken.None);

        Assert.Contains(towns, town => town.Gate is null);
        Assert.Contains(towns, town => town.Gate is not null);
    }

    /// <remarks>
    /// A permission block sits between the map path and the area kind and carries its own
    /// integers. Reading them as area operands would silently place characters at a
    /// required level rather than at the gate position.
    /// </remarks>
    [SkippableFact]
    public async Task A_permission_block_does_not_leak_into_the_area_operands()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfTownDefinitionSource(provider);

        var town = await source.GetAsync(17, CancellationToken.None);

        Assert.NotNull(town);
        var gate = town!.Gate;
        Assert.NotNull(gate);
        Assert.Equal(474, gate!.Operand1);
        Assert.Equal(234, gate.Operand2);
    }

    [SkippableFact]
    public async Task A_map_states_its_transit_rectangles_and_walkable_rectangles()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfTownDefinitionSource(provider);

        var town = await source.GetAsync(17, CancellationToken.None);
        var area = town!.AreaAt(4);
        Assert.NotNull(area);

        var geometry = await source.GetMapGeometryAsync(area!.MapPath, CancellationToken.None);

        Assert.NotNull(geometry);
        Assert.NotEmpty(geometry!.TransitAreas);
        Assert.NotEmpty(geometry.WalkableAreas);
        Assert.Contains(geometry.TransitAreas, transit => transit.IsLocal);
        Assert.Contains(geometry.TransitAreas, transit => !transit.IsLocal);
    }

    [SkippableFact]
    public async Task A_map_the_content_does_not_define_resolves_to_no_geometry()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfTownDefinitionSource(provider);

        var geometry = await source.GetMapGeometryAsync(
            "Town/NoSuchTown/NoSuchMap.map",
            CancellationToken.None);

        Assert.Null(geometry);
    }

    [SkippableFact]
    public async Task A_town_the_content_does_not_define_resolves_to_null()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfTownDefinitionSource(provider);

        Assert.Null(await source.GetAsync(31337, CancellationToken.None));
    }

    [SkippableFact]
    public async Task A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog()
    {
        using var provider = new PvfArchiveProvider(
            new PvfContentOptions { ArchivePath = "/definitely/not/here/Script.pvf" });
        var source = new PvfTownDefinitionSource(provider);

        // Resolution falls through to the conventional locations, so this only proves the
        // failure shape when no archive is reachable at all.
        Skip.If(ContentArchive.Available, "An archive is reachable from this working tree.");

        var error = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await source.GetAllAsync(CancellationToken.None));

        Assert.Contains("Script.pvf", error.Message, StringComparison.Ordinal);
    }
}
