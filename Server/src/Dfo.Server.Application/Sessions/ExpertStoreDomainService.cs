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
    private readonly Dictionary<CharacterId, StoreOwner> open = [];
    private readonly Dictionary<CharacterId, ExpertStoreVisit> visitors = [];
    private readonly Dictionary<CharacterId, PendingClosures> reserved = [];
    private long lastInstance;

    public void Open(CharacterId ownerCharacterId)
    {
        GameplayWriteAssertion.Writing(nameof(ExpertStoreDomainService) + "." + nameof(Open));
        SetLegacyOpen(ownerCharacterId, null);
    }

    public void Open(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(ExpertStoreDomainService) + "." + nameof(Open));
        SetLegacyOpen(session.CharacterId, session);
    }

    private void SetLegacyOpen(CharacterId owner, SelectedSessionIdentity? session)
    {
        lock (gate)
        {
            DemandUnreserved(owner);
            if (open.TryGetValue(owner, out var existing)) Remove(existing, owner);
            open[owner] = new(session, null);
        }
    }

    public bool Close(CharacterId ownerCharacterId)
    {
        GameplayWriteAssertion.Writing(nameof(ExpertStoreDomainService) + "." + nameof(Close));
        lock (gate)
        {
            if (!open.TryGetValue(ownerCharacterId, out var current)) return false;
            DemandUnreserved(ownerCharacterId);
            Remove(current, ownerCharacterId);
            return true;
        }
    }

    public bool CloseExact(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(ExpertStoreDomainService) + "." + nameof(CloseExact));
        return CloseSession(session).Closed;
    }

    public ExpertStoreClosure? CloseOwned(SelectedSessionIdentity exactOwner)
    {
        GameplayWriteAssertion.Writing(nameof(ExpertStoreDomainService) + "." + nameof(CloseOwned));
        return CloseSession(exactOwner).Closure;
    }

    private (bool Closed, ExpertStoreClosure? Closure) CloseSession(SelectedSessionIdentity session)
    {
        lock (gate)
        {
            var owned = open.GetValueOrDefault(session.CharacterId);
            if (owned is null || owned.Session != session)
            {
                LeaveLocked(session);
                return (false, null);
            }

            DemandUnreserved(session.CharacterId);
            var closure = owned.Store is { } store ? CaptureClosure(store) : null;
            LeaveLocked(session);
            Remove(owned, session.CharacterId);
            return (true, closure);
        }
    }

    public bool HasOpenStore(CharacterId ownerCharacterId)
    {
        lock (gate) return open.ContainsKey(ownerCharacterId);
    }

    public bool HasOpenStore(SelectedSessionIdentity exactOwner)
    {
        lock (gate) return open.GetValueOrDefault(exactOwner.CharacterId)?.Session == exactOwner;
    }

    public ExpertStoreSnapshot? TryOpen(SelectedSessionIdentity owner, TownAreaKey area, ExpertStorePlacement placement)
    {
        GameplayWriteAssertion.Writing(nameof(ExpertStoreDomainService) + "." + nameof(TryOpen));
        lock (gate)
        {
            if (open.ContainsKey(owner.CharacterId)) return null;
            DemandUnreserved(owner.CharacterId);
            var store = new ExpertStoreSnapshot(checked(++lastInstance), owner, area, placement);
            open.Add(owner.CharacterId, new(owner, store));
            return store;
        }
    }

    public ExpertStoreSnapshot? FindOwned(SelectedSessionIdentity owner)
    {
        lock (gate)
            return open.GetValueOrDefault(owner.CharacterId) is { } current && current.Session == owner
                ? current.Store : null;
    }

    public IReadOnlyList<ExpertStoreSnapshot> In(TownAreaKey area)
    {
        lock (gate)
            return open.Values.Select(owner => owner.Store).OfType<ExpertStoreSnapshot>()
                .Where(store => store.Area == area).OrderBy(store => store.Owner.CharacterId.Value).ToArray();
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
            Pending().Schedule(expected.Owner.CharacterId, open[expected.Owner.CharacterId]);
            return CaptureClosure(expected);
        }
    }

    public ExpertStoreClosure? StageCloseOwned(SelectedSessionIdentity exactOwner)
    {
        GameplayWriteAssertion.Writing(nameof(ExpertStoreDomainService) + "." + nameof(StageCloseOwned));
        lock (gate)
        {
            if (!open.TryGetValue(exactOwner.CharacterId, out var owned) || owned.Session != exactOwner) return null;
            Pending().Schedule(exactOwner.CharacterId, owned);
            return owned.Store is { } store ? CaptureClosure(store) : null;
        }
    }

    private PendingClosures Pending() => (transactions.Current
        ?? throw new InvalidOperationException("Staging expert store closure requires the asset transaction."))
        .Enlist(this, () => new PendingClosures(this));

    private bool IsCurrent(ExpertStoreSnapshot store) =>
        ReferenceEquals(open.GetValueOrDefault(store.Owner.CharacterId)?.Store, store);

    private bool LeaveLocked(SelectedSessionIdentity visitor) =>
        visitors.TryGetValue(visitor.CharacterId, out var entry) && entry.Visitor == visitor
            && visitors.Remove(visitor.CharacterId);

    private ExpertStoreClosure CaptureClosure(ExpertStoreSnapshot store) => new(store,
        visitors.Values.Where(visit => ReferenceEquals(visit.Store, store))
            .Select(visit => visit.Visitor).OrderBy(visitor => visitor.CharacterId.Value).ToArray());

    private void Remove(StoreOwner owned, CharacterId character)
    {
        open.Remove(character);
        if (owned.Store is not { } store) return;
        foreach (var entry in visitors.ToArray())
            if (ReferenceEquals(entry.Value.Store, store))
                visitors.Remove(entry.Key);
    }

    private void DemandUnreserved(CharacterId owner)
    {
        if (reserved.ContainsKey(owner))
            throw new InvalidOperationException("An asset transaction is preparing to close this expert store.");
    }

    private sealed record StoreOwner(SelectedSessionIdentity? Session, ExpertStoreSnapshot? Store);

    private sealed class PendingClosures(ExpertStoreDomainService registry) : ITransactionParticipant
    {
        private readonly Dictionary<CharacterId, StoreOwner> closures = [];
        public bool CommitCanFail => false;

        public void Schedule(CharacterId owner, StoreOwner expected) => closures[owner] = expected;

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
                    registry.Remove(expected, owner);
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
