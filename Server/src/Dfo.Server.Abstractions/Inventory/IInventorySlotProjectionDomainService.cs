using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Read-only protocol projection of an already-decided business slot.</summary>
/// <remarks>
/// Notification composers may only depend on seam-layer domain services.  Keeping this projection
/// separate from <see cref="IBusinessInventorySlotDomainService"/> prevents the container-binding
/// service used by domain behavior from exposing protocol coordinates.
/// </remarks>
public interface IInventorySlotProjectionDomainService
{
    GameSlot Project(BusinessSlot slot);
}
