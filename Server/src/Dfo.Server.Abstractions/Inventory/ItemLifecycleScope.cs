using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>One transaction's detached-item custody, shared by durable and volatile stores.</summary>
public sealed class ItemLifecycleScope : ITransactionParticipant
{
    private static readonly object Key = new();
    private readonly Dictionary<ItemRecord, Custody> pending = new(ReferenceEqualityComparer.Instance);
    private bool closed;
    private sealed record Custody(object Issuer, Func<CancellationToken, ValueTask> Delete,
        Func<int, CancellationToken, ValueTask<(ItemRecord Taken, ItemRecord Remainder)>> Split);
    private ItemLifecycleScope() { }
    public static ItemLifecycleScope In(IDomainTransaction transaction) =>
        transaction.Enlist(Key, static () => new ItemLifecycleScope());
    public bool CommitCanFail => false;
    public void EnsureActive()
    {
        if (closed) throw new InvalidOperationException("The item's transaction has ended.");
    }
    /// <summary>Storage implementation hook; issuance alone does not create or import a row.</summary>
    internal ItemRecord Issue(ItemSnapshot snapshot, object issuer, Func<CancellationToken, ValueTask> delete,
        Func<int, CancellationToken, ValueTask<(ItemRecord Taken, ItemRecord Remainder)>> split)
    {
        EnsureActive();
        var record = new ItemRecord(snapshot);
        pending.Add(record,new(issuer,delete,split));
        return record;
    }
    internal bool IsPending(ItemRecord record) => !closed && pending.ContainsKey(record);
    internal bool IsForeign(ItemRecord record, object receiver)
    {
        var custody = Require(record);
        var foreign = !ReferenceEquals(custody.Issuer,receiver);
        if (foreign && !record.IsPile) throw new InvalidOperationException("Only interchangeable piles can cross storage backends.");
        return foreign;
    }
    /// <summary>Called only after the receiving store has successfully materialized its custody.</summary>
    internal async ValueTask AcceptAsync(ItemRecord record, object receiver, CancellationToken ct)
    {
        var custody = Require(record);
        if (IsForeign(record,receiver)) await custody.Delete(ct).ConfigureAwait(false);
        pending.Remove(record);
    }
    /// <summary>Deletes only the current transaction's outstanding detached record, at its issuer.</summary>
    public async ValueTask DeleteItemAsync(ItemRecord record, CancellationToken ct)
    {
        var custody = Require(record);
        await custody.Delete(ct).ConfigureAwait(false);
        pending.Remove(record);
    }
    public ValueTask<(ItemRecord Taken, ItemRecord Remainder)> SplitAsync(ItemRecord record, int count, CancellationToken ct)
    {
        var custody = Require(record);
        if (!record.IsPile || count <= 0 || count >= record.Count) throw new ArgumentOutOfRangeException(nameof(count));
        return custody.Split(count,ct);
    }
    private Custody Require(ItemRecord record)
    {
        EnsureActive();
        return pending.TryGetValue(record,out var custody) ? custody
            : throw new InvalidOperationException("The record belongs to another transaction or has already been received or deleted.");
    }
    public void ValidateForCommit()
    {
        EnsureActive();
        if (pending.Count != 0) throw new InvalidOperationException("Every detached item must be placed or explicitly deleted before commit.");
    }
    public ValueTask PrepareAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); ValidateForCommit(); return ValueTask.CompletedTask; }
    public ValueTask CommitAsync(CancellationToken ct) { Close(); return ValueTask.CompletedTask; }
    public ValueTask RollbackAsync(CancellationToken ct) { Close(); return ValueTask.CompletedTask; }
    private void Close() { closed = true; pending.Clear(); }
}
