using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Persistence.Inventory;

/// <summary>Run-lifetime storage; changes join the same transaction as permanent item custody.</summary>
internal sealed class RunItemDomainStore : IItemDomainStore
{
    private readonly State state = new();
    public object StoreIdentity => this;
    public bool IsDurable => false;
    public IItemDomainStoreSession SessionIn(IDomainTransaction transaction)
    {
        var scope = ItemLifecycleScope.In(transaction);
        return transaction.Enlist(StoreIdentity, () => new Session(state, scope));
    }
    private sealed class State
    {
        public Dictionary<(ContainerId, int), ItemSnapshot> Items = [];
        public long Version;
        public int NextUid = 1;
    }
    private sealed class Session : IItemDomainStoreSession, ITransactionParticipant
    {
        private readonly State state;
        private readonly ItemLifecycleScope scope;
        private readonly object identity = new();
        private readonly Dictionary<(ContainerId, int), long> generations = [];
        private readonly Dictionary<(ContainerId, int), ItemSnapshot> working;
        private readonly HashSet<ItemRecord> issued = new(ReferenceEqualityComparer.Instance);
        private readonly long version;
        private int nextUid;
        private bool changed;
        private bool closed;
        private bool prepared;
        public Session(State state, ItemLifecycleScope scope)
        {
            this.state = state; this.scope = scope;
            lock (state)
            {
                version = state.Version; nextUid = state.NextUid;
                working = state.Items.ToDictionary(x => x.Key, x => x.Value.DeepCopy());
            }
        }
        public bool CommitCanFail => false;
        private void Active(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); scope.EnsureActive();
            if (closed) throw new InvalidOperationException("The item session has ended.");
        }
        private long Generation(ContainerId c, int slot) => generations.GetValueOrDefault((c, slot));
        private ItemReference Reference(ContainerId c, int slot) => new(identity, c, slot, Generation(c, slot), working[(c, slot)]);
        private ItemSnapshot Resolve(ItemReference reference, CancellationToken ct)
        {
            Active(ct);
            if (!reference.BelongsTo(identity) || reference.Generation != Generation(reference.ContainerId, reference.StorageSlotIndex)
                || !working.TryGetValue((reference.ContainerId, reference.StorageSlotIndex), out var item))
                throw new InvalidOperationException("The item reference no longer identifies this occupancy.");
            return item;
        }
        public ValueTask<ItemReference?> GetAsync(ContainerId c, int slot, CancellationToken ct)
        {
            Active(ct); CheckSlot(slot);
            return ValueTask.FromResult(working.ContainsKey((c, slot)) ? Reference(c, slot) : null);
        }
        public ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(ContainerId c, CancellationToken ct)
        {
            Active(ct);
            return ValueTask.FromResult<IReadOnlyList<ItemReference>>([.. working.Keys.Where(k => k.Item1 == c).OrderBy(k => k.Item2).Select(k => Reference(c, k.Item2))]);
        }
        public ValueTask<ItemRecord> CreateAsync(ItemSnapshot snapshot, CancellationToken ct)
        {
            Active(ct); Validate(snapshot);
            if (snapshot.ItemUid is not null) throw new InvalidOperationException("Creation cannot supply an existing identity.");
            if (!snapshot.IsPile)
            {
                var uid = checked(nextUid++); snapshot = snapshot with { ItemUid = new ItemIdentity(uid) };
                if (snapshot.CreatureDetail is not null) snapshot = snapshot with { InstanceValue = uid };
            }
            changed = true;
            return ValueTask.FromResult(Issue(snapshot));
        }
        public ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct)
        {
            var old = Resolve(reference, ct); var amount = count ?? old.Count;
            if (amount <= 0 || amount > old.Count || (!old.IsPile && amount != 1)) throw new ArgumentOutOfRangeException(nameof(count));
            var key = (reference.ContainerId, reference.StorageSlotIndex);
            if (amount == old.Count) { working.Remove(key); generations[key] = checked(Generation(key.Item1, key.Item2) + 1); }
            else working[key] = old with { Count = old.Count - amount };
            changed = true;
            return ValueTask.FromResult(Issue(old with { Count = amount }));
        }
        public async ValueTask<ItemReference> PutAsync(ContainerId c, int slot, ItemRecord record, CancellationToken ct)
        {
            Active(ct); CheckSlot(slot);
            var foreign = scope.IsForeign(record, identity);
            if (!foreign && !issued.Contains(record)) throw new InvalidOperationException("The record was not issued by this store.");
            var snapshot = record.Snapshot;
            ValidatePosition(c, slot, snapshot);
            var key = (c, slot);
            if (working.TryGetValue(key, out var held))
            {
                if (!held.CanStackWith(snapshot)) throw new InvalidOperationException("The destination does not accept this item.");
                snapshot = held with { Count = checked(held.Count + snapshot.Count) };
            }
            else generations[key] = checked(Generation(c, slot) + 1);
            // If source release fails the surrounding use case must roll back all participants.
            await scope.AcceptAsync(record, identity, ct).ConfigureAwait(false);
            working[key] = snapshot.DeepCopy(); issued.Remove(record); changed = true;
            return Reference(c, slot);
        }
        public ValueTask<ItemReference> UpdateAsync(ItemReference reference, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct)
        {
            var old = Resolve(reference, ct); var next = change(old.DeepCopy()).DeepCopy(); Validate(next);
            ValidatePosition(reference.ContainerId, reference.StorageSlotIndex, next);
            if (next.IsMirror != old.IsMirror || next.MirrorSourceUid != old.MirrorSourceUid
                || next.ItemUid != old.ItemUid || next.IsPile != old.IsPile || next.Type != old.Type || next.LegacyKind != old.LegacyKind
                || (next.ItemId != old.ItemId && (next.IsPile || next.Type != ItemTraits.Creature || next.LegacyKind != 5))
                || (!next.IsPile && next.Type is ItemTraits.Avatar or ItemTraits.Creature && next.InstanceValue != old.InstanceValue)) throw new InvalidOperationException("Update changed identity shape.");
            working[(reference.ContainerId, reference.StorageSlotIndex)] = next; changed = true;
            var result = Reference(reference.ContainerId, reference.StorageSlotIndex);
            return ValueTask.FromResult(reference.BusinessSlot is { } business ? result.WithBusinessSlot(business) : result);
        }
        public ValueTask DeleteItemAsync(ItemRecord record, CancellationToken ct)
        {
            Active(ct);
            if (!issued.Contains(record)) throw new InvalidOperationException("The record was not issued by this store.");
            return scope.DeleteItemAsync(record, ct);
        }
        private ItemRecord Issue(ItemSnapshot snapshot)
        {
            ItemRecord? record = null;
            record = scope.Issue(snapshot, identity, ct =>
            {
                Active(ct); issued.Remove(record!); changed = true; return ValueTask.CompletedTask;
            }, async (count, ct) =>
            {
                Active(ct);
                await scope.AcceptAsync(record!, identity, ct).ConfigureAwait(false);
                issued.Remove(record!);
                return (Issue(snapshot with { Count = count }), Issue(snapshot with { Count = snapshot.Count - count }));
            });
            issued.Add(record); return record;
        }
        public ValueTask ApplyDecidedWritesAsync(CancellationToken ct) { Active(ct); return ValueTask.CompletedTask; }
        public ValueTask PrepareAsync(CancellationToken ct)
        {
            Active(ct); scope.ValidateForCommit();
            lock (state)
                if (changed && state.Version != version) throw new InvalidOperationException("The volatile store changed concurrently.");
            prepared = true; return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken ct)
        {
            if (!prepared) throw new InvalidOperationException("Prepare is required.");
            lock (state)
                if (changed) { state.Items = working; state.NextUid = nextUid; state.Version++; }
            Close(); return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken ct) { Close(); return ValueTask.CompletedTask; }
        private void Close() { closed = true; issued.Clear(); generations.Clear(); }
        private static void CheckSlot(int slot) { if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot)); }
        private static bool IsRevivalCounter(ItemSnapshot item) =>
            item is { IsPile: true, LegacyKind: 0, ItemId: 1, Type: ItemTraits.None };
        private static void ValidatePosition(ContainerId container, int slot, ItemSnapshot item)
        {
            if (item.IsMirror && (item.IsPile || !container.TryGetCharacterOwner(out _, out var mirrorList)
                || mirrorList != InventoryContainerType.Equipment))
                throw new InvalidDataException("A mirror may only occupy a character equipment slot.");
            if (item.Count == 0 && (!IsRevivalCounter(item) || slot != 1
                || !container.TryGetCharacterOwner(out _, out var type) || type != InventoryContainerType.Main))
                throw new InvalidDataException("A zero counter can only occupy the character revival slot.");
        }
        private static void Validate(ItemSnapshot item)
        {
            if (item.AppearanceItemId is < 0 || item.AppearanceItemId == 0 && item.LegacyKind != 8) throw new InvalidDataException("Invalid item appearance identity.");
            if (item.Type != ItemTraitClassification.For(item.LegacyKind, item.ItemId))
                throw new InvalidDataException("Item type disagrees with its legacy encoding.");
            if (item.ItemId <= 0 || (item.Count < 0 || item.Count == 0 && !IsRevivalCounter(item)) || (!item.IsPile && item.Count != 1) || (item.IsPile && item.ItemUid is not null))
                throw new InvalidDataException("Invalid item identity or quantity.");
            if ((!item.IsPile && item.Type == ItemTraits.Avatar) != (item.AvatarDetail is not null)
                || (!item.IsPile && item.Type == ItemTraits.Creature) != (item.CreatureDetail is not null))
                throw new InvalidDataException("Instance details do not match the item type.");
            if (!item.IsPile && item.Type == ItemTraits.Avatar && item.InstanceValue <= 0)
                throw new InvalidDataException("An avatar requires a positive legacy identity.");
            if (item.AvatarDetail is { JewelSocket.Length: not 30 }) throw new InvalidDataException("Invalid avatar socket size.");
            if (item.CreatureDetail is { } pet && (pet.CreatureText.Length > 4096 || pet.Field04 is < 0 or > 255 || pet.ProgressValue < 0
                || pet.FieldAfterValue is < 0 or > 255 || pet.Mode1Field0A is < 0 or > 255 || pet.Mode1Field0B is < 0 or > 255))
                throw new InvalidDataException("Invalid creature details.");
        }
    }
}

internal sealed class TransientItemDomainStoreFactory : ITransientItemDomainStoreFactory
{
    public IItemDomainStore Create() => new RunItemDomainStore();
}
