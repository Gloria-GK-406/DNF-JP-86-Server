using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfInventorySortKeySourceTests
{
    [SkippableFact]
    public async Task Actual_archive_resolves_equipment_and_stackable_sort_keys()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfInventorySortKeySource(provider, new PvfEquipmentSlotDefinitionSource(provider));
        var equipment = await source.GetAsync(100310669, default);
        Assert.InRange(equipment.Level, 1, 100);
        Assert.InRange(equipment.Rarity, 0, 6);
        Assert.NotEqual(int.MaxValue, equipment.Part);
        var stackable = await source.GetAsync(3037, default);
        Assert.NotEmpty(stackable.StackableType);
        Assert.Same(equipment, await source.GetAsync(100310669, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => source.GetAsync(-1, default).AsTask());
    }
}
