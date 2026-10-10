using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Application.Inventory;

internal sealed class InventoryContentsEventDomainService(IEventService events,
    IDomainTransactionAccessor transactions) : IInventoryContentsEventDomainService
{
    public async ValueTask RecordAsync(ContainerId container, CancellationToken ct)
    {
        var transaction = transactions.Current
            ?? throw new InvalidOperationException("Inventory facts require the item's transaction.");
        var batch = transaction.Enlist(this, static () => new Batch());
        if (batch.Containers.Add(container))
            await events.EnqueueAsync(new InventoryContentsChanged(container), ct);
    }

    private sealed class Batch : ITransactionParticipant
    {
        public HashSet<ContainerId> Containers { get; } = [];
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
}
