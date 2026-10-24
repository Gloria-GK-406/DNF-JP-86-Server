using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>Explicit non-request input for isolated domain tests, scoped to one invocation.</summary>
internal static class TestEffectFootprints
{
    internal sealed class Lease(SelectedSessionIdentity owner) : IEffectFootprintRecorder, IDisposable
    {
        private readonly Guid job = GameplayJob.Current?.Id
            ?? throw new InvalidOperationException("The test explicitly creates its footprint on a job.");
        private bool closed;
        public void DemandActive(SelectedSessionIdentity value)
        {
            if (closed || GameplayJob.Current?.Id != job || value != owner)
                throw new InvalidOperationException("The explicit test footprint is closed or has a different owner/job.");
            GameplayJob.DemandHealthy();
        }
        public void RecordInventorySlots(SelectedSessionIdentity value, IReadOnlyList<BusinessSlot> slots) => DemandActive(value);
        public void RecordRentalState(SelectedSessionIdentity value) => DemandActive(value);
        public void Dispose() => closed = true;
    }

    internal static async ValueTask<T> Run<T>(SelectedSessionIdentity owner,
        Func<IEffectFootprintRecorder, ValueTask<T>> action)
    {
        using var lease = new Lease(owner);
        return await action(lease).ConfigureAwait(false);
    }

    internal static async ValueTask<TResult> Invoke<TCommand, TResult>(ICommandHandler<TCommand, TResult> handler,
        TCommand command, CancellationToken ct)
        where TCommand : class, ICommand<TResult>, IRequestSettlementCommand where TResult : notnull
    {
        using var lease = new Lease(command.Session ?? throw new InvalidOperationException("This test requires an owner."));
        var context = new CommandExecutionContext(Guid.NewGuid(), typeof(TCommand), handler.GetType(), CommandExecutionKind.Transactional);
        context.SupplyLegacyFootprint(lease);
        return await handler.HandleAsync(context, command, ct).ConfigureAwait(false);
    }
}
