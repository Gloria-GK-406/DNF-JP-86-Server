using System.Collections.Frozen;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Commands;

internal sealed class CommandSelectedSessionCleanup(ISelectedSessionCleanupDomainService cleanup,
    IIllusionTowerDomainService? illusion = null, IBossTowerDomainService? boss = null) : ICommandSelectedSessionCleanup
{
    public void RecordDisconnectedDeliveryFailure(CommandExecutionContext context, Exception cause)
    {
        var request = context.Request;
        if (context.CommandType != typeof(DisconnectedLifecycleCommand) || request is not CommandRequestContext.TrustedInternal
            { Origin.Kind: GameplayCallSource.Lifecycle, Origin.SourceConnection: null, DisconnectedLifecycleCleanup: true }
            || request.IsClosed || GameplayJob.Current?.Id != ((CommandRequestContext.TrustedInternal)request).Origin.Chain.Id)
            throw new InvalidOperationException("Only the live disconnected lifecycle may record its fixed cleanup delivery failure.");
        request.Failed(cause);
    }

    public ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(CommandExecutionContext context,
        SelectedSessionIdentity session, bool includeOwner, CommandWorkUnit? action = null,
        CancellationToken cancellationToken = default)
    {
        var request = context.Request ?? throw new InvalidOperationException("An explicit cleanup request is required.");
        object invocation;
        try { invocation = request.BeginSelectedSessionCleanup(action); }
        catch (Exception error) { request.Failed(error); GameplayJob.RecordProgramError(error); throw; }
        // Freeze before any await. Leave can settle the remaining exact members, not just its actor.
        var owners = new HashSet<SelectedSessionIdentity> { session };
        try
        {
            if (illusion?.Find(session) is { } tower) owners.UnionWith(tower.Members.Select(member => member.Session));
            if (boss?.Find(session) is { } towerBoss) owners.UnionWith(towerBoss.Members.Select(member => member.Session));
        }
        catch (Exception error) { request.EndSelectedSessionCleanup(invocation); request.Failed(error); throw; }
        var operation = RunAsync().AsTask();
        request.TrackOperation(operation);
        action?.TrackCleanup(operation);
        return new(operation);

        async ValueTask<SelectedSessionCleanupNotificationPlan> RunAsync()
        {
            using var footprint = new CleanupFootprint(request, invocation, action, owners.ToFrozenSet());
            try
            {
                return await cleanup.CleanupAsync(session, includeOwner, footprint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) { request.Failed(error); throw; }
            finally { request.EndSelectedSessionCleanup(invocation); }
        }
    }

    private sealed class CleanupFootprint(CommandRequestContext request, object invocation,
        CommandWorkUnit? action, FrozenSet<SelectedSessionIdentity> owners) : IEffectFootprintRecorder, IDisposable
    {
        private bool closed;
        private void Demand(SelectedSessionIdentity owner)
        {
            try
            {
                request.DemandSelectedSessionCleanup(invocation, action);
                if (closed || owner is null || !owners.Contains(owner))
                    throw new InvalidOperationException("The cleanup footprint has ended or does not own this exact session.");
            }
            catch (Exception error) { request.Failed(error); GameplayJob.RecordProgramError(error); throw; }
        }
        public void DemandActive(SelectedSessionIdentity owner) => Demand(owner);
        public void DemandEffects(EffectFootprintOwner owner)
        {
            if (owner?.Session is not { } session || session.AccountId != owner.AccountId || session.CharacterId != owner.CharacterId)
            {
                var error = new InvalidOperationException("Cleanup requires an exact matching effect owner.");
                request.Failed(error); GameplayJob.RecordProgramError(error); throw error;
            }
            Demand(session);
        }
        public void RecordEffects(EffectFootprintOwner owner, IReadOnlyList<AppliedEffect> applied)
        { DemandEffects(owner); ArgumentNullException.ThrowIfNull(applied); }
        public void RecordInventorySlots(SelectedSessionIdentity owner, IReadOnlyList<BusinessSlot> slots)
        { Demand(owner); ArgumentNullException.ThrowIfNull(slots); }
        public void RecordRentalState(SelectedSessionIdentity owner) => Demand(owner);
        public void Dispose() => closed = true;
    }
}
