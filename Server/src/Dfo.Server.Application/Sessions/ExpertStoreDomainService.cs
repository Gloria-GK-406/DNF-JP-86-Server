using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Sessions;

/// <summary>Keeps open expert stores and their visits in one process-local registry.</summary>
internal sealed class ExpertStoreDomainService(IDomainTransactionAccessor transactions) : IExpertStoreDomainService
{
    private readonly Lock gate = new();
    private readonly Dictionary<CharacterId, ExpertStoreSnapshot> open = [];
    private readonly Dictionary<CharacterId, ExpertStoreVisit> visitors = [];
    private readonly Dictionary<CharacterId, PendingClosures> reserved = [];
    private long lastInstance;

    public ExpertStoreClosure? CloseOwned(SelectedSessionIdentity exactOwner)
    {
        GameplayWriteAssertion.Writing(nameof(ExpertStoreDomainService) + "." + nameof(CloseOwned));
        lock (gate)
        {
            var store = FindOwnedLocked(exactOwner);
            if (store is null)
            {
                LeaveLocked(exactOwner);
                return null;
            }

            DemandUnreserved(exactOwner.CharacterId);
            var closure = CaptureClosure(store);
            LeaveLocked(exactOwner);
            Remove(store);
            return closure;
        }
    }

    public bool HasOpenStore(CharacterId ownerCharacterId)
    {
        lock (gate) return open.ContainsKey(ownerCharacterId);
    }

    public bool HasOpenStore(SelectedSessionIdentity exactOwner)
    {
        lock (gate) return FindOwnedLocked(exactOwner) is not null;
    }

    public ExpertStoreSnapshot? TryOpen(SelectedSessionIdentity owner, TownAreaKey area, ExpertStorePlacement placement)
    {
        GameplayWriteAssertion.Writing(nameof(ExpertStoreDomainService) + "." + nameof(TryOpen));
        lock (gate)
        {
            if (open.ContainsKey(owner.CharacterId)) return null;
            DemandUnreserved(owner.CharacterId);
            var store = new ExpertStoreSnapshot(checked(++lastInstance), owner, area, placement);
            open.Add(owner.CharacterId, store);
            return store;
        }
    }

    public ExpertStoreSnapshot? FindOwned(SelectedSessionIdentity owner)
    {
        lock (gate) return FindOwnedLocked(owner);
    }

    private ExpertStoreSnapshot? FindOwnedLocked(SelectedSessionIdentity owner) =>
        open.GetValueOrDefault(owner.CharacterId) is { } store && store.Owner == owner ? store : null;

    public IReadOnlyList<ExpertStoreSnapshot> In(TownAreaKey area)
    {
        lock (gate)
            return open.Values.Where(store => store.Area == area).OrderBy(store => store.Owner.CharacterId.Value).ToArray();
    }

    public bool Enter(SelectedSessionIdentity visitor, ExpertStoreSnapshot store)
    {
        GameplayWriteAssertion.Writing(nameof(ExpertStoreDomainService) + "." + nameof(Enter));
        lock (gate)
        {
            if (!IsCurrent(store) || reserved.ContainsKey(store.Owner.CharacterId)) return false;
            visitors[visitor.CharacterId] = new(visitor, store);
            return true;
        }
    }

    public ExpertStoreVisit? Visiting(SelectedSessionIdentity visitor)
    {
        lock (gate)
            return visitors.TryGetValue(visitor.CharacterId, out var visit) && visit.Visitor == visitor
                && IsCurrent(visit.Store) ? visit : null;
    }

    public bool Leave(SelectedSessionIdentity visitor)
    {
        GameplayWriteAssertion.Writing(nameof(ExpertStoreDomainService) + "." + nameof(Leave));
        lock (gate) return LeaveLocked(visitor);
    }

    public ExpertStoreClosure? StageClose(ExpertStoreSnapshot expected)
    {
        GameplayWriteAssertion.Writing(nameof(ExpertStoreDomainService) + "." + nameof(StageClose));
        lock (gate)
        {
            if (!IsCurrent(expected)) return null;
            Pending().Schedule(expected);
            return CaptureClosure(expected);
        }
    }

    public ExpertStoreClosure? StageCloseOwned(SelectedSessionIdentity exactOwner)
    {
        GameplayWriteAssertion.Writing(nameof(ExpertStoreDomainService) + "." + nameof(StageCloseOwned));
        lock (gate)
        {
            if (FindOwnedLocked(exactOwner) is not { } store) return null;
            Pending().Schedule(store);
            return CaptureClosure(store);
        }
    }

    private PendingClosures Pending() => (transactions.Current
        ?? throw new InvalidOperationException("Staging expert store closure requires the asset transaction."))
        .Enlist(this, () => new PendingClosures(this));

    private bool IsCurrent(ExpertStoreSnapshot store) =>
        ReferenceEquals(open.GetValueOrDefault(store.Owner.CharacterId), store);

    private bool LeaveLocked(SelectedSessionIdentity visitor) =>
        visitors.TryGetValue(visitor.CharacterId, out var entry) && entry.Visitor == visitor
            && visitors.Remove(visitor.CharacterId);

    private ExpertStoreClosure CaptureClosure(ExpertStoreSnapshot store) => new(store,
        visitors.Values.Where(visit => ReferenceEquals(visit.Store, store))
            .Select(visit => visit.Visitor).OrderBy(visitor => visitor.CharacterId.Value).ToArray());

    private void Remove(ExpertStoreSnapshot store)
    {
        open.Remove(store.Owner.CharacterId);
        foreach (var entry in visitors.ToArray())
            if (ReferenceEquals(entry.Value.Store, store))
                visitors.Remove(entry.Key);
    }

    private void DemandUnreserved(CharacterId owner)
    {
        if (reserved.ContainsKey(owner))
            throw new InvalidOperationException("An asset transaction is preparing to close this expert store.");
    }

    private sealed class PendingClosures(ExpertStoreDomainService registry) : ITransactionParticipant
    {
        private readonly Dictionary<CharacterId, ExpertStoreSnapshot> closures = [];
        public bool CommitCanFail => false;

        public void Schedule(ExpertStoreSnapshot expected) => closures[expected.Owner.CharacterId] = expected;

        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            lock (registry.gate)
            {
                foreach (var (owner, expected) in closures)
                {
                    registry.DemandUnreserved(owner);
                    if (!ReferenceEquals(registry.open.GetValueOrDefault(owner), expected))
                        throw new InvalidOperationException("The expert store was replaced before settlement.");
                }
                foreach (var owner in closures.Keys) registry.reserved.Add(owner, this);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            lock (registry.gate)
                foreach (var (owner, expected) in closures)
                {
                    registry.Remove(expected);
                    registry.reserved.Remove(owner);
                }
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            lock (registry.gate)
                foreach (var owner in closures.Keys)
                    if (ReferenceEquals(registry.reserved.GetValueOrDefault(owner), this))
                        registry.reserved.Remove(owner);
            return ValueTask.CompletedTask;
        }
    }
}
