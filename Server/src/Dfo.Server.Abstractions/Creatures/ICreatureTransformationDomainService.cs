using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Creatures;

/// <summary>A same-UID creature item change staged inside the caller's domain transaction.</summary>
public sealed record CreatureTransformationReceipt(
    CreatureState Before,
    CreatureState After,
    BusinessSlot Slot,
    ItemSnapshot Core)
{
    public int CreatureId { get; init; }
}

public interface ICreatureTransformationDomainService
{
    /// <summary>Turn one inventory egg into its declared output creature.</summary>
    ValueTask<CreatureTransformationReceipt?> HatchAsync(
        SelectedSessionIdentity session, BusinessSlot eggSlot, CancellationToken cancellationToken);

    /// <summary>Apply the one unambiguous, quest-free automatic evolution of the worn creature.</summary>
    ValueTask<CreatureTransformationReceipt?> EvolveAsync(
        SelectedSessionIdentity session, CancellationToken cancellationToken);
}
