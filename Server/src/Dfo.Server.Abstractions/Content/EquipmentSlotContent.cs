using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Content;

/// <summary>
/// Which slot of the equipment container a piece is worn in.
/// </summary>
/// <remarks>
/// The archive answers this directly: every wearable declares <c>[equipment type]</c>, and the name
/// it gives -- <c>[shoulder]</c>, <c>[weapon]</c>, <c>[coat]</c> -- is the same vocabulary the
/// character-creation table uses to say which slot a starting piece fills. The slot numbers are
/// therefore one table, read twice, rather than two tables that agree until they do not.
/// </remarks>
public interface IEquipmentSlotDefinitionSource
{
    /// <summary>
    /// The slot this item is worn in, or null when the archive declares nothing wearable under
    /// that identifier.
    /// </summary>
    ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken cancellationToken);
}
