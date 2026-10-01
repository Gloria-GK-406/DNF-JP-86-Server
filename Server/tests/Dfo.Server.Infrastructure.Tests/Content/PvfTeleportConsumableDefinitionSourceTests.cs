using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfTeleportConsumableDefinitionSourceTests
{
    [SkippableFact]
    public async Task Real_potion_selection_destinations_and_Shonan_gate_are_typed()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfTeleportConsumableDefinitionSource(provider);
        var potion = await source.GetAsync(2600014, default);
        Assert.NotNull(potion);
        Assert.Equal([1,2,3,4,5,6,9,11,14,18,20,21,22], potion.TownIds);
        Assert.Null(await source.GetAsync(10006007, default));
        var use = await new PvfItemUseRuleDefinitionSource(provider).GetAsync(2600014, default);
        Assert.NotNull(use); Assert.True(use.IsReadable);
        Assert.Equal(60, use.MinimumLevel); Assert.Equal(10000, use.CooldownMilliseconds);
        var town = await new PvfTownDefinitionSource(provider).GetAsync(11, default);
        Assert.NotNull(town?.Gate);
        Assert.Equal((3, 474, 234, 69), (town.Gate.AreaId, town.Gate.Operand1, town.Gate.Operand2, town.Gate.MinimumLevel));
    }
}
