namespace Dfo.Server.Abstractions.Content;

/// <summary>One position in a collection box, and the item that fills it.</summary>
/// <param name="Column">Zero-based column within the box grid.</param>
/// <param name="Row">Zero-based row within the box grid.</param>
/// <param name="ItemTemplateId">The item this position accepts.</param>
/// <param name="Tier">
/// The content's own grading of the position. The scale is the content's; what a tier
/// entitles a character to is a rule.
/// </param>
public readonly record struct CollectionBoxSlot(int Column, int Row, int ItemTemplateId, int Tier);

/// <summary>An item that extends a box's expiry, and how many are consumed.</summary>
public readonly record struct CollectionBoxExtension(int ItemTemplateId, int Count);

/// <summary>
/// A collection box as the content defines it: its grid, the item each position accepts,
/// and the dates bounding its life. Which positions a character has filled is progress
/// and lives elsewhere.
/// </summary>
public sealed record CollectionBoxDefinition(
    int BoxIndex,
    int Columns,
    int Rows,
    DateTimeOffset? BaseExpiresAt,
    DateTimeOffset? MaximumExpiresAt,
    IReadOnlyList<CollectionBoxSlot> Slots,
    IReadOnlyList<CollectionBoxExtension> Extensions)
{
    /// <summary>Positions the grid declares, whether or not an item was assigned to each.</summary>
    public int Capacity => Columns * Rows;

    /// <summary>The item a position accepts, or null when the grid does not define it.</summary>
    public CollectionBoxSlot? SlotAt(int column, int row)
    {
        foreach (var slot in Slots)
        {
            if (slot.Column == column && slot.Row == row)
                return slot;
        }

        return null;
    }
}

/// <summary>Static collection-box definitions.</summary>
public interface ICollectionBoxDefinitionSource
{
    /// <summary>Every box the content defines, in declaration order.</summary>
    ValueTask<IReadOnlyList<CollectionBoxDefinition>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>One box, or null when the content does not define it.</summary>
    ValueTask<CollectionBoxDefinition?> GetAsync(int boxIndex, CancellationToken cancellationToken);
}
