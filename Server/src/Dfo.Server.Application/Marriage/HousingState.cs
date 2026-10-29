using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

/// <summary>Persistent shared assets and an exact-session decoration editor lease.</summary>
internal sealed class HousingState(IMarriageHousingDomainStore store, ISelectedSessionDomainService sessions, IOnlineCharacterRuntimeDomainService onlineCharacters,
    IDomainTransactionAccessor transactions) : IMarriageHousingSessionDomainService
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, SelectedSessionIdentity> editors = [];
    private readonly Dictionary<SelectedSessionIdentity, Dfo.Server.Data.Characters.CharacterPositionState> returns = [];
    public void RememberReturn(SelectedSessionIdentity actor, Dfo.Server.Data.Characters.CharacterPositionState position)
    {
        GameplayWriteAssertion.Writing(nameof(HousingState) + "." + nameof(RememberReturn));
        lock (gate) { if (!CoupleRoomTown.Is(position.TownId)) returns[actor] = position; }
    }
    public Dfo.Server.Data.Characters.CharacterPositionState? ReturnFor(SelectedSessionIdentity actor)
    { lock (gate) return returns.GetValueOrDefault(actor); }
    public void CloseExact(SelectedSessionIdentity actor)
    {
        GameplayWriteAssertion.Writing(nameof(HousingState) + "." + nameof(CloseExact));
        lock (gate) { foreach (var key in editors.Where(x => x.Value == actor).Select(x => x.Key).ToArray()) editors.Remove(key); returns.Remove(actor); }
    }

    public ValueTask<HousingSnapshot?> ReadAsync(Guid relation, CancellationToken ct) => store.ReadAsync(relation, ct);
    public ValueTask<HousingSnapshot> EnsureAsync(Guid relation, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(HousingState) + "." + nameof(EnsureAsync));
        return store.EnsureAsync(relation, ct);
    }
    public ValueTask SaveAsync(HousingSnapshot snapshot, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(HousingState) + "." + nameof(SaveAsync));
        return store.SaveAsync(snapshot, ct);
    }
    public ValueTask<bool> RecordHarvestAsync(Guid planting, int round, IReadOnlyList<HousingReward> rewards, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(HousingState) + "." + nameof(RecordHarvestAsync));
        return store.RecordHarvestAsync(planting, round, rewards, ct);
    }
    public bool IsInside(SelectedSessionIdentity actor, HousingSnapshot room) => sessions.IsInWorld(actor)
        && onlineCharacters.StandingOf(actor) is { Area.TownId: CoupleRoomTown.Id, Area.AreaId: 0 } member
        && member.Area.Instance == room.Instance;
    public bool Edit(Guid relation, SelectedSessionIdentity actor, bool close)
    {
        GameplayWriteAssertion.Writing(nameof(HousingState) + "." + nameof(Edit));
        lock (gate)
        {
            if (editors.TryGetValue(relation, out var holder))
            {
                if (holder == actor)
                {
                    if (close) PendingEdit(relation, actor, true);
                    return true;
                }
                if (sessions.IsInWorld(holder) && onlineCharacters.StandingOf(holder) is { Area.TownId: CoupleRoomTown.Id }) return close;
                editors.Remove(relation);
            }
            if (!close) PendingEdit(relation, actor, false);
            return true;
        }
    }
    public bool CanEdit(Guid relation, SelectedSessionIdentity actor)
    { lock (gate) return editors.TryGetValue(relation, out var holder) && holder == actor; }
    private bool IsPresent(SelectedSessionIdentity actor) => sessions.IsInWorld(actor)
        && onlineCharacters.StandingOf(actor) is { Area.TownId: CoupleRoomTown.Id };

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
