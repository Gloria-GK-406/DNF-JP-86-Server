using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfCollectionBoxDefinitionSourceTests
{
    [SkippableFact]
    public async Task Projects_every_box_the_table_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCollectionBoxDefinitionSource(provider);

        var boxes = await source.GetAllAsync(CancellationToken.None);

        Assert.NotEmpty(boxes);
        Assert.All(boxes, box => Assert.True(box.BoxIndex > 0));
        Assert.All(boxes, box => Assert.True(box.Columns > 0 && box.Rows > 0));
    }

    [SkippableFact]
    public async Task A_boxs_grid_holds_exactly_the_positions_it_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCollectionBoxDefinitionSource(provider);

        foreach (var box in await source.GetAllAsync(CancellationToken.None))
        {
            Assert.Equal(box.Capacity, box.Slots.Count);
            Assert.All(box.Slots, slot => Assert.InRange(slot.Column, 0, box.Columns - 1));
            Assert.All(box.Slots, slot => Assert.InRange(slot.Row, 0, box.Rows - 1));
            Assert.All(box.Slots, slot => Assert.True(slot.ItemTemplateId > 0));

            // Row-major order must produce one slot per position, with none repeated.
            var positions = box.Slots.Select(slot => (slot.Column, slot.Row)).ToHashSet();
            Assert.Equal(box.Slots.Count, positions.Count);
        }
    }

    [SkippableFact]
    public async Task Projects_a_known_box_field_by_field()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCollectionBoxDefinitionSource(provider);

        var box = await source.GetAsync(1, CancellationToken.None);
        Skip.If(box is null, "This archive does not define collection box 1.");

        Assert.Equal(5, box!.Columns);
        Assert.Equal(3, box.Rows);
        Assert.Equal(15, box.Slots.Count);

        var first = box.SlotAt(0, 0);
        Assert.NotNull(first);
        Assert.Equal(10007322, first!.Value.ItemTemplateId);
        Assert.Equal(1, first.Value.Tier);

        var extension = Assert.Single(box.Extensions);
        Assert.Equal(10007344, extension.ItemTemplateId);
        Assert.Equal(1, extension.Count);

        Assert.NotNull(box.MaximumExpiresAt);
        Assert.Equal(2015, box.MaximumExpiresAt!.Value.Year);
    }

    [SkippableFact]
    public async Task A_box_with_no_extension_item_reports_none_rather_than_a_placeholder()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCollectionBoxDefinitionSource(provider);

        var box = await source.GetAsync(2, CancellationToken.None);
        Skip.If(box is null, "This archive does not define collection box 2.");

        Assert.Empty(box!.Extensions);
    }

    [SkippableFact]
    public async Task An_undefined_box_returns_null()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCollectionBoxDefinitionSource(provider);

        Assert.Null(await source.GetAsync(int.MaxValue, CancellationToken.None));
    }

    /// <summary>
    /// SC-6, matrix rows 32 and 33: the packet carries a box's progress, which is the grid
    /// combined with the positions a character has filled. The rule stays with G5; this
    /// proves the definition carries what that rule consumes.
    /// </summary>
    [SkippableFact]
    public async Task Definitions_support_projecting_a_characters_box_progress()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCollectionBoxDefinitionSource(provider);

        var box = Assert.IsType<CollectionBoxDefinition>(
            (await source.GetAllAsync(CancellationToken.None))[0]);

        // Stand-in for the saved slots G3 will supply.
        var filled = box.Slots.Take(4).Select(slot => slot.ItemTemplateId).ToHashSet();

        var progress = box.Slots
            .Select(slot => new
            {
                slot.Column,
                slot.Row,
                slot.ItemTemplateId,
                Collected = filled.Contains(slot.ItemTemplateId),
            })
            .ToList();

        Assert.Equal(box.Capacity, progress.Count);
        Assert.Equal(4, progress.Count(entry => entry.Collected));
        Assert.Contains(progress, entry => !entry.Collected);
    }
}
