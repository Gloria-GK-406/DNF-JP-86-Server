using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Effects;

/// <summary>Temporary captured legacy input; no method looks up a current request.</summary>
internal sealed class LegacyEffectFootprint(
    NetworkCommandAuthorization.Snapshot authorization,
    Action inheritedGuard,
    Func<string?> unavailable,
    Action<SelectedSessionIdentity> demandOwner,
    Action<IReadOnlyList<BusinessSlot>> inventory,
    Action rental) : IEffectFootprintRecorder, IDisposable
{
    private readonly Guid job = GameplayJob.Current?.Id
        ?? throw new InvalidOperationException("A footprint begins on a gameplay job.");
    private int closed;

    internal void DemandAvailable()
    {
        if (Volatile.Read(ref closed) != 0 || GameplayJob.Current?.Id != job)
            throw new InvalidOperationException("The footprint invocation is closed or belongs to another job.");
        GameplayJob.DemandHealthy();
        inheritedGuard();
        authorization.DemandOpen();
        if (unavailable() is { } reason) throw new InvalidOperationException(reason);
        authorization.DemandAuthority();
    }

    public void DemandActive(SelectedSessionIdentity owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        DemandAvailable();
        demandOwner(owner);
    }

    public void RecordInventorySlots(SelectedSessionIdentity owner, IReadOnlyList<BusinessSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        DemandActive(owner);
        inventory(slots);
    }

    public void RecordRentalState(SelectedSessionIdentity owner)
    {
        DemandActive(owner);
        inventory([]);
        rental();
    }

    public void Dispose() => Interlocked.Exchange(ref closed, 1);
}

internal sealed record LegacyFootprintInheritance(bool IsEmpty, Action DemandOpen);
internal sealed record LegacyInvocationOrigin(bool Standalone, Action DemandOpen);
