using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Application.Inventory;

internal sealed class InventoryContentsEventDomainService(IEventService events,
    IDomainTransactionAccessor transactions) : IInventoryContentsEventDomainService
{
    public async ValueTask RecordAsync(ContainerId container, IReadOnlyCollection<int> changedItemIds, CancellationToken ct)
    {
        var transaction = transactions.Current
            ?? throw new InvalidOperationException("Inventory facts require the item's transaction.");
        var batch = transaction.Enlist(this, static () => new Batch());
        // Enqueue serializes immediately: freeze each new ID batch instead of mutating an earlier payload.
        var ids = changedItemIds.Where(id => id > 0 && batch.Items.Add((container, id))).ToImmutableArray();
        if (ids.Length > 0)
            await events.EnqueueAsync(new InventoryContentsChanged(container) { ChangedItemIds = ids }, ct);
    }

    private sealed class Batch : ITransactionParticipant
    {
        public HashSet<(ContainerId, int)> Items { get; } = [];
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
}
