using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// The store whose items do not outlive the process.
/// </summary>
/// <remarks>
/// What a dungeon hands the player is kept here. Because nothing is written anywhere, its session
/// commits by swapping state it already holds, which cannot fail -- so any number of these may take
/// part in one transaction beside the single durable one.
/// </remarks>
internal sealed class MemoryItemStore : IItemStore
{
    private readonly Dictionary<ContainerId, Dictionary<int, StoredItem>> containers = [];

    private readonly object sync = new();

    public object StoreIdentity => this;

    public bool IsDurable => false;

    public IItemStoreSession SessionIn(IDomainTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return transaction.Enlist(
            this.StoreIdentity,
            () => new MemoryItemStoreSession(this.containers, this.sync));
    }
}

/// <summary>One use case's reads and writes against items held in memory.</summary>
/// <remarks>
/// The held writes are turned into the containers' replacement contents while preparing, so that
/// committing is one assignment per touched container and has nothing left in it that could fail.
/// That is what lets any number of these sit beside the single participant whose commit still can.
/// </remarks>
internal sealed class MemoryItemStoreSession(
    Dictionary<ContainerId, Dictionary<int, StoredItem>> containers,
    object sync) : IItemStoreSession, ITransactionParticipant
{
    /// <summary>What each named slot will hold, or null where it will be left empty.</summary>
    private readonly Dictionary<ContainerId, Dictionary<int, StoredItem>> staged = [];

    private readonly HashSet<(ContainerId Container, int Slot)> emptied = [];

    /// <summary>Each touched container's whole replacement contents, built while preparing.</summary>
    private Dictionary<ContainerId, Dictionary<int, StoredItem>>? prepared;

    public bool CommitCanFail => false;

    public ValueTask<IReadOnlyList<StoredItem>> ReadAllAsync(
        ContainerId containerId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var items = new SortedDictionary<int, StoredItem>();
        lock (sync)
        {
            if (containers.TryGetValue(containerId, out var held))
            {
                foreach (var (slot, item) in held)
                {
                    items[slot] = item;
                }
            }
        }

        foreach (var (slot, item) in this.Overlay(containerId))
        {
            if (item is null)
            {
                items.Remove(slot);
            }
            else
            {
                items[slot] = item;
            }
        }

        return ValueTask.FromResult<IReadOnlyList<StoredItem>>([.. items.Values]);
    }

    public ValueTask<StoredItem?> ReadAsync(
        ContainerId containerId, int slotIndex, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (this.staged.TryGetValue(containerId, out var written)
            && written.TryGetValue(slotIndex, out var item))
        {
            return ValueTask.FromResult<StoredItem?>(item);
        }

        if (this.emptied.Contains((containerId, slotIndex)))
        {
            return ValueTask.FromResult<StoredItem?>(null);
        }

        lock (sync)
        {
            return ValueTask.FromResult(
                containers.TryGetValue(containerId, out var held)
                    && held.TryGetValue(slotIndex, out var stored)
                    ? stored
                    : null);
        }
    }

    public void Write(ContainerId containerId, StoredItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateSlot(item.SlotIndex);
        this.emptied.Remove((containerId, item.SlotIndex));
        if (!this.staged.TryGetValue(containerId, out var written))
        {
            written = [];
            this.staged.Add(containerId, written);
        }

        written[item.SlotIndex] = item;
    }

    public void Clear(ContainerId containerId, int slotIndex)
    {
        ValidateSlot(slotIndex);
        if (this.staged.TryGetValue(containerId, out var written))
        {
            written.Remove(slotIndex);
        }

        this.emptied.Add((containerId, slotIndex));
    }

    public ValueTask PrepareAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var touched = this.staged.Keys.Concat(this.emptied.Select(slot => slot.Container)).ToHashSet();
        var replacement = new Dictionary<ContainerId, Dictionary<int, StoredItem>>();
        lock (sync)
        {
            foreach (var containerId in touched)
            {
                var contents = containers.TryGetValue(containerId, out var held)
                    ? new Dictionary<int, StoredItem>(held)
                    : [];
                foreach (var (slot, item) in this.Overlay(containerId))
                {
                    if (item is null)
                    {
                        contents.Remove(slot);
                    }
                    else
                    {
                        contents[slot] = item;
                    }
                }

                replacement.Add(containerId, contents);
            }
        }

        this.prepared = replacement;
        return ValueTask.CompletedTask;
    }

    /// <remarks>
    /// A commit reached without a prepare is refused rather than working the contents out here.
    /// Doing that would put allocation back into the one act this participant promises cannot fail,
    /// and dropping the changes instead would surface as lost items rather than as an exception.
    /// </remarks>
    public ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (this.prepared is null)
        {
            var unprepared = this.staged.Count > 0 || this.emptied.Count > 0;
            this.Discard();
            return unprepared
                ? ValueTask.FromException(new InvalidOperationException(
                    "The item store was committed before it was prepared, so it changed nothing."))
                : ValueTask.CompletedTask;
        }

        lock (sync)
        {
            foreach (var (containerId, contents) in this.prepared)
            {
                containers[containerId] = contents;
            }
        }

        this.Discard();
        return ValueTask.CompletedTask;
    }

    public ValueTask RollbackAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        this.Discard();
        return ValueTask.CompletedTask;
    }

    private void Discard()
    {
        this.prepared = null;
        this.staged.Clear();
        this.emptied.Clear();
    }

    /// <summary>The slot numbers a container has, which are the same ones the tables accept.</summary>
    private static void ValidateSlot(int slotIndex)
    {
        if (slotIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex), slotIndex, "A slot of a container is never negative.");
        }
    }

    /// <summary>This session's own changes to one container, as slot to item or to nothing.</summary>
    private IEnumerable<(int Slot, StoredItem? Item)> Overlay(ContainerId containerId)
    {
        foreach (var slot in this.emptied)
        {
            if (slot.Container == containerId)
            {
                yield return (slot.Slot, null);
            }
        }

        if (this.staged.TryGetValue(containerId, out var written))
        {
            foreach (var (slot, item) in written)
            {
                yield return (slot, item);
            }
        }
    }
}
