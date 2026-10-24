using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Effects;

internal sealed class LifecycleEffectFootprintSource : ILifecycleEffectFootprintSource
{
    public ILifecycleEffectFootprintLease Open(InternalCommandOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        origin.Validate();
        GameplayJob.DemandHealthy();
        if (origin.Kind != GameplayCallSource.Lifecycle || origin.SourceConnection is not null
            || GameplayJob.Current is not { Source: GameplayCallSource.Lifecycle } job
            || origin.Chain.Id != job.Id || origin.Chain.RootId != job.RootId
            || origin.Chain.ParentId != job.ParentId || origin.Chain.Depth != job.Depth
            || origin.Chain.Source != job.Source || origin.Chain.SourceConnection != job.SourceConnection)
            throw new InvalidOperationException("Cleanup requires the explicit current lifecycle origin.");
        return new Lease(job.Id);
    }

    private sealed class Lease(Guid job) : ILifecycleEffectFootprintLease, IEffectFootprintRecorder
    {
        private int closed;
        public IEffectFootprintRecorder Footprint => this;
        private void Demand()
        {
            if (Volatile.Read(ref closed) != 0 || GameplayJob.Current?.Id != job)
                throw new InvalidOperationException("The lifecycle footprint is closed or belongs to another job.");
            GameplayJob.DemandHealthy();
        }
        public void DemandEffects(EffectFootprintOwner owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            if (owner.Session is { } session && (session.AccountId != owner.AccountId || session.CharacterId != owner.CharacterId))
                throw new InvalidOperationException("The effects session differs from its owner.");
            Demand();
        }
        public void RecordEffects(EffectFootprintOwner owner, IReadOnlyList<AppliedEffect> applied)
        { ArgumentNullException.ThrowIfNull(applied); DemandEffects(owner); }
        public void DemandActive(SelectedSessionIdentity owner)
        { ArgumentNullException.ThrowIfNull(owner); DemandEffects(new(owner.AccountId, owner.CharacterId, owner)); }
        public void RecordInventorySlots(SelectedSessionIdentity owner, IReadOnlyList<BusinessSlot> slots)
        { ArgumentNullException.ThrowIfNull(slots); DemandActive(owner); }
        public void RecordRentalState(SelectedSessionIdentity owner) => DemandActive(owner);
        public void Dispose() => Interlocked.Exchange(ref closed, 1);
    }
}
