using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Application.World;

internal sealed class DungeonParticipationDomainService(IDomainTransactionAccessor transactions) : IDungeonParticipationDomainService
{
    private readonly object gate = new();
    private readonly Dictionary<CharacterId, DungeonParticipationOwner> members = [];
    private readonly Dictionary<CharacterId, Changes> reservations = [];
    private Changes Pending => (transactions.Current ?? throw new InvalidOperationException("Participation changes require a transaction."))
        .Enlist(this, () => new Changes(this));
    public DungeonParticipationOwner? Find(CharacterId id)
    {
        if (transactions.Current is { IsReadOnly: false }) return Pending.Find(id);
        lock (gate) return members.GetValueOrDefault(id);
    }
    public bool TryClaim(IReadOnlyList<CharacterId> ids, DungeonParticipationOwner owner)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonParticipationDomainService) + "." + nameof(TryClaim));
        if (ids.Count == 0 || ids.Distinct().Count() != ids.Count || ids.Any(x => Find(x) is { } current && current != owner
            && !(current.Mode == DungeonParticipationMode.Ordinary && owner.Mode == DungeonParticipationMode.Ordinary))) return false;
        foreach (var id in ids) Pending.Set(id, owner);
        return true;
    }
    public void Release(CharacterId id, DungeonParticipationOwner expected)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonParticipationDomainService) + "." + nameof(Release));
        if (transactions.Current is null) { ReleaseCommitted(id, expected); return; }
        if (Find(id) == expected) Pending.Set(id, null);
    }
    public void ReleaseCommitted(CharacterId id, DungeonParticipationOwner expected)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonParticipationDomainService) + "." + nameof(ReleaseCommitted));
        lock (gate) if (members.GetValueOrDefault(id) == expected) members.Remove(id);
    }
    private sealed class Changes(DungeonParticipationDomainService owner) : ITransactionParticipant
    {
        private readonly Dictionary<CharacterId, (DungeonParticipationOwner? Before, DungeonParticipationOwner? After)> changes = [];
        public bool CommitCanFail => false;
        public DungeonParticipationOwner? Find(CharacterId id)
        {
            if (changes.TryGetValue(id, out var change)) return change.After;
            lock (owner.gate) return owner.members.GetValueOrDefault(id);
        }
        public void Set(CharacterId id, DungeonParticipationOwner? next)
        {
            if (changes.TryGetValue(id, out var prior)) changes[id] = (prior.Before, next);
            else lock (owner.gate) changes[id] = (owner.members.GetValueOrDefault(id), next);
        }
        public ValueTask PrepareAsync(CancellationToken ct)
        {
            lock (owner.gate)
            {
                if (changes.Any(x => owner.members.GetValueOrDefault(x.Key) != x.Value.Before
                    || owner.reservations.TryGetValue(x.Key, out var held) && !ReferenceEquals(held, this)))
                    throw new InvalidOperationException("Dungeon participation changed before commit.");
                foreach (var id in changes.Keys) owner.reservations[id] = this;
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken ct)
        {
            lock (owner.gate) foreach (var (id, (_, after)) in changes)
            {
                if (after is null) owner.members.Remove(id); else owner.members[id] = after;
                if (owner.reservations.GetValueOrDefault(id) == this) owner.reservations.Remove(id);
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken ct)
        {
            lock (owner.gate) foreach (var id in changes.Keys)
                if (owner.reservations.GetValueOrDefault(id) == this) owner.reservations.Remove(id);
            return ValueTask.CompletedTask;
        }
    }
}
