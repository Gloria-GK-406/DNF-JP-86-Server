using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

/// <summary>Persistent shared assets and an exact-session decoration editor lease.</summary>
internal sealed class HousingState(IMarriageHousingDomainStore store, ISelectedSessionDomainService sessions, ITownAreaDomainService areas,
    IDomainTransactionAccessor transactions) : IMarriageHousingSessionDomainService
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, SelectedSessionIdentity> editors = [];
    private readonly Dictionary<SelectedSessionIdentity, Dfo.Server.Data.Characters.CharacterPositionState> returns = [];
    public void RememberReturn(SelectedSessionIdentity actor, Dfo.Server.Data.Characters.CharacterPositionState position)
    { lock (gate) { if (position.TownId != 101) returns[actor] = position; } }
    public Dfo.Server.Data.Characters.CharacterPositionState? ReturnFor(SelectedSessionIdentity actor)
    { lock (gate) return returns.GetValueOrDefault(actor); }
    public void CloseExact(SelectedSessionIdentity actor)
    { lock (gate) { foreach (var key in editors.Where(x => x.Value == actor).Select(x => x.Key).ToArray()) editors.Remove(key); returns.Remove(actor); } }

    public ValueTask<HousingSnapshot?> ReadAsync(Guid relation, CancellationToken ct) => store.ReadAsync(relation, ct);
    public ValueTask<HousingSnapshot> EnsureAsync(Guid relation, CancellationToken ct) => store.EnsureAsync(relation, ct);
    public ValueTask SaveAsync(HousingSnapshot snapshot, CancellationToken ct) => store.SaveAsync(snapshot, ct);
    public ValueTask<bool> RecordHarvestAsync(Guid planting, int round, IReadOnlyList<HousingReward> rewards, CancellationToken ct) => store.RecordHarvestAsync(planting, round, rewards, ct);
    public bool IsInside(SelectedSessionIdentity actor, HousingSnapshot room) => sessions.IsInWorld(actor)
        && areas.Find(actor.CharacterId) is { Area.TownId: 101, Area.AreaId: 0 } member
        && member.Lease == actor.TownAreaLease() && member.Area.Instance == room.Instance;
    public bool Edit(Guid relation, SelectedSessionIdentity actor, bool close)
    {
        lock (gate)
        {
            if (editors.TryGetValue(relation, out var holder))
            {
                if (holder == actor)
                {
                    if (close) PendingEdit(relation, actor, true);
                    return true;
                }
                if (sessions.IsInWorld(holder) && areas.Find(holder.CharacterId) is { Area.TownId: 101 } m && m.Lease == holder.TownAreaLease()) return close;
                editors.Remove(relation);
            }
            if (!close) PendingEdit(relation, actor, false);
            return true;
        }
    }
    public bool CanEdit(Guid relation, SelectedSessionIdentity actor)
    { lock (gate) return editors.TryGetValue(relation, out var holder) && holder == actor; }
    private bool IsPresent(SelectedSessionIdentity actor) => sessions.IsInWorld(actor)
        && areas.Find(actor.CharacterId) is { Area.TownId: 101 } member && member.Lease == actor.TownAreaLease();

    private void PendingEdit(Guid relation, SelectedSessionIdentity actor, bool close) =>
        (transactions.Current ?? throw new InvalidOperationException("Housing editing requires its account transaction."))
            .Enlist(this, () => new EditorChange(this)).Changes[relation] = (actor, close);

    private sealed class EditorChange(HousingState owner) : ITransactionParticipant
    {
        public Dictionary<Guid, (SelectedSessionIdentity Actor, bool Close)> Changes { get; } = [];
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct)
        {
            lock (owner.gate)
            {
                foreach (var (relation, change) in Changes)
                {
                    if (change.Close)
                    { if (owner.editors.GetValueOrDefault(relation) == change.Actor) owner.editors.Remove(relation); }
                    else if (owner.IsPresent(change.Actor))
                        owner.editors[relation] = change.Actor;
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}
