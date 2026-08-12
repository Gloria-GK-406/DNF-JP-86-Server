using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfCreatureDefinitionSourceTests
{
    [SkippableFact]
    public async Task Projects_every_creature_the_list_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCreatureDefinitionSource(provider);

        var creatures = await source.GetAllAsync(CancellationToken.None);

        Assert.NotEmpty(creatures);
        Assert.All(creatures, creature => Assert.True(creature.CreatureId > 0));
        Assert.All(creatures, creature => Assert.NotNull(creature.Name));

        // Some creatures declare an empty name, so requiring one everywhere would encode a
        // rule the archive does not follow.
        Assert.Contains(creatures, creature => creature.Name.Length > 0);
        Assert.All(creatures, creature => Assert.True(creature.MaximumLevel >= creature.StartLevel));
    }

    [SkippableFact]
    public async Task Projects_a_known_creature_field_by_field()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCreatureDefinitionSource(provider);

        var creature = await source.GetAsync(1, CancellationToken.None);
        Skip.If(creature is null, "This archive does not define creature 1.");

        // creature/Faras/faras.cre
        Assert.Equal(1, creature!.StartLevel);
        Assert.Equal(1, creature.PermissionLevel);
        Assert.Equal(50, creature.MaximumLevel);
        Assert.Equal(["red", "blue", "green"], creature.ArtifactSlots);
    }

    /// <summary>
    /// SC-6, matrix row 58: the greeting packet is sent only when the equipped creature has
    /// a usable script body, so an absent script must read as absent rather than as an
    /// empty string the projection would happily send.
    /// </summary>
    [SkippableFact]
    public async Task Ambient_lines_are_present_for_scripted_creatures_and_absent_otherwise()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCreatureDefinitionSource(provider);
        var creatures = await source.GetAllAsync(CancellationToken.None);

        var scripted = creatures.Where(creature => creature.HasAmbientLines).ToList();
        var unscripted = creatures.Where(creature => !creature.HasAmbientLines).ToList();

        Assert.NotEmpty(scripted);
        Assert.NotEmpty(unscripted);
        Assert.All(scripted, creature => Assert.All(creature.AmbientLines, line => Assert.NotEmpty(line)));
        Assert.All(unscripted, creature => Assert.Empty(creature.AmbientLines));
    }

    [SkippableFact]
    public async Task Ambient_lines_exclude_the_town_specific_blocks_nested_below_them()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCreatureDefinitionSource(provider);

        var creature = await source.GetAsync(1, CancellationToken.None);
        Skip.If(creature is null, "This archive does not define creature 1.");
        Skip.IfNot(creature!.HasAmbientLines, "Creature 1 has no companion script in this archive.");

        // faras.wrd lists three general lines, then per-town blocks that are separate content.
        Assert.Equal(3, creature.AmbientLines.Count);
    }

    [SkippableFact]
    public async Task Evolution_steps_pair_their_operands_positionally()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCreatureDefinitionSource(provider);
        var creatures = await source.GetAllAsync(CancellationToken.None);

        var evolving = creatures.Where(creature => creature.Evolutions.Count > 0).ToList();
        Skip.If(evolving.Count == 0, "No creature in this archive declares an evolution.");

        Assert.All(evolving, creature =>
            Assert.All(creature.Evolutions, step => Assert.True(step.EvolvesIntoCreatureId > 0)));

        // Zero-valued placeholder tags must not become evolutions into creature zero.
        Assert.All(creatures, creature =>
            Assert.DoesNotContain(creature.Evolutions, step => step.EvolvesIntoCreatureId == 0));
    }

    [SkippableFact]
    public async Task Evolution_target_equipment_resolvability_comes_from_the_creature_equipment_index()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCreatureDefinitionSource(provider);
        var creatures = await source.GetAllAsync(CancellationToken.None);

        var resolvedTarget = 0;
        foreach (var targetCreatureId in creatures
                     .SelectMany(creature => creature.Evolutions)
                     .Select(evolution => evolution.EvolvesIntoCreatureId)
                     .Distinct())
        {
            if (await source.CanResolveEvolutionEquipmentAsync(
                    currentEquipmentItemId: 0,
                    targetCreatureId: targetCreatureId,
                    cancellationToken: CancellationToken.None))
            {
                resolvedTarget = targetCreatureId;
                break;
            }
        }

        Assert.True(resolvedTarget > 0);
        Assert.False(await source.CanResolveEvolutionEquipmentAsync(
            currentEquipmentItemId: 0,
            targetCreatureId: int.MaxValue,
            cancellationToken: CancellationToken.None));
    }

    [SkippableFact]
    public async Task Distinct_equipment_output_resolves_even_when_the_target_creature_is_unmapped()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCreatureDefinitionSource(provider);
        var outputBackedCurrentItemId = 0;

        foreach (var (itemTemplateId, _) in PvfListFile.Read(
                     provider.Archive,
                     "equipment/equipment.lst"))
        {
            if (await source.CanResolveEvolutionEquipmentAsync(
                    itemTemplateId,
                    targetCreatureId: int.MaxValue,
                    cancellationToken: CancellationToken.None))
            {
                outputBackedCurrentItemId = itemTemplateId;
                break;
            }
        }

        // int.MaxValue is deliberately absent from the creature-equipment map, so a true
        // answer can only come from a distinct valid output on the current equipment.
        Assert.True(outputBackedCurrentItemId > 0);
    }

    [SkippableFact]
    public async Task An_undefined_creature_returns_null()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCreatureDefinitionSource(provider);

        Assert.Null(await source.GetAsync(int.MaxValue, CancellationToken.None));
    }
}
