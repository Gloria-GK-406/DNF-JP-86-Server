using System.Collections.Immutable;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World.IllusionTower;

internal sealed class TowerRoom(TowerRoomSnapshot state)
{
    public TowerRoomSnapshot State { get; private set; } = state;
    public TowerRoom Clone() => new(State);
    public bool Load(CharacterId character, ImmutableArray<TowerMember> members)
    {
        if (State.Phase != TowerRoomPhase.Loading || State.LoadedMembers.Contains(character)) return false;
        var loaded = State.LoadedMembers.Add(character);
        State = State with { LoadedMembers = loaded,
            Phase = members.All(x => loaded.Contains(x.Session.CharacterId)) ? TowerRoomPhase.Ready : TowerRoomPhase.Loading };
        return true;
    }
    public bool Start(DateTimeOffset now)
    {
        if (State.Phase != TowerRoomPhase.Ready) return false;
        State = State with { Phase = TowerRoomPhase.Fighting, StartedAt = now };
        return true;
    }
    public TowerActor? Kill(ushort uid, DateTimeOffset now)
    {
        var actor = State.Actors.FirstOrDefault(x => x.Uid == uid);
        if (State.Phase != TowerRoomPhase.Fighting || actor is null || actor.Dead) return null;
        State = State with { Actors = State.Actors.Replace(actor, actor with { Dead = true }),
            Items = State.Items.Select(item => item.ActorUid == uid ? item with { Available = true } : item).ToImmutableArray() };
        if (State.Actors.All(x => x.Dead || !x.Seed.BlocksClear))
            State = State with { Phase = TowerRoomPhase.Cleared, ClearedAt = now };
        return actor;
    }
    public bool Claim(ushort uid)
    {
        var item = State.Items.SingleOrDefault(x => x.Uid == uid && x.Available && !x.Claimed);
        if (item is null) return false;
        State = State with { Items = State.Items.Replace(item, item with { Claimed = true }) };
        return true;
    }
    public void RemoveMember(CharacterId character, ImmutableArray<TowerMember> remaining)
    {
        State = State with { LoadedMembers = State.LoadedMembers.Remove(character) };
        if (State.Phase == TowerRoomPhase.Loading && remaining.All(x => State.LoadedMembers.Contains(x.Session.CharacterId)))
            State = State with { Phase = TowerRoomPhase.Ready };
    }
}

internal sealed class IllusionTowerDgn
{
    public required Guid RunId { get; init; }
    public required int DungeonId { get; init; }
    public required int LevelCount { get; init; }
    public required uint Seed { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required ImmutableArray<TowerMember> Members { get; set; }
    public required TowerRoom Room { get; set; }
    public ImmutableArray<int> CompletedLevels { get; private set; } = [];
    public TowerCompletion? Completion { get; private set; }
    public byte? ReturnOption { get; private set; }
    public DateTimeOffset? ReturnDueAt { get; private set; }
    public int NextUid { get; private set; } = 1;
    public required ImmutableHashSet<ushort> ReservedUids { get; init; }

    public IllusionTowerDgn Clone() => new()
    {
        RunId = RunId, DungeonId = DungeonId, LevelCount = LevelCount, Seed = Seed, StartedAt = StartedAt,
        Members = Members, Room = Room.Clone(), CompletedLevels = CompletedLevels,
        Completion = Completion, ReturnOption = ReturnOption, ReturnDueAt = ReturnDueAt,
        NextUid = NextUid, ReservedUids = ReservedUids
    };
    public IllusionTowerSnapshot Snapshot() => new(RunId, DungeonId, Members, Room.State, CompletedLevels, Completion, StartedAt)
    {
        TotalLevels = LevelCount,
        ReturnOption = ReturnOption,
        ReturnDueAt = ReturnDueAt,
    };
    public TowerActionResult Result(bool changed, TowerActor? killed = null) => new(changed, Snapshot(), killed);
    public void SetRoom(TowerRoomSeed seed, long version)
    {
        ushort Allocate()
        {
            while (NextUid <= ushort.MaxValue && ReservedUids.Contains((ushort)NextUid)) NextUid++;
            if (NextUid > ushort.MaxValue) throw new InvalidOperationException("Tower actor UID space exhausted.");
            return (ushort)NextUid++;
        }
        if (seed.Actors.All(x => !x.BlocksClear)) throw new InvalidDataException("A tower room requires a blocking actor.");
        var actors = seed.Actors.Select(x => new TowerActor(Allocate(), x, false)).ToImmutableArray();
        var items = actors.SelectMany(actor => actor.Seed.Items.Select(item => new TowerGroundItem(Allocate(), actor.Uid, item, false, false))).ToImmutableArray();
        var objects = seed.Objects.Select(x => new TowerObject(Allocate(), x)).ToImmutableArray();
        Room = new(new(seed.Level, seed.MapId, seed.Seed, version, TowerRoomPhase.Loading,
            actors, objects, ImmutableHashSet<CharacterId>.Empty) { Items = items });
    }
    private void End(TowerEndReason reason, DateTimeOffset now) => Completion ??= new(reason, CompletedLevels.Length,
        reason == TowerEndReason.Completed ? LevelCount : Math.Max(1, Room.State.Level - 1), false, now);
    public TowerActionResult Load(SelectedSessionIdentity session) => Result(Completion is null && Room.Load(session.CharacterId, Members));
    public TowerActionResult Start(SelectedSessionIdentity session, DateTimeOffset now) =>
        Result(Completion is null && Members.Any(x => x.Session == session && x.IsLeader) && Room.Start(now));
    public TowerActionResult Kill(SelectedSessionIdentity session, ushort uid, DateTimeOffset now)
    {
        if (Completion is not null || Members.Any(x => x.Session == session && x.Dead)) return Result(false);
        var killed = Room.Kill(uid, now);
        if (killed is null) return Result(false);
        if (Room.State.Phase == TowerRoomPhase.Cleared)
        {
            CompletedLevels = CompletedLevels.Add(Room.State.Level);
            if (Room.State.Level == LevelCount) End(TowerEndReason.Completed, now);
        }
        return Result(true, killed) with { Drops = Room.State.Items.Where(item => item.ActorUid == uid && item.Available && !item.Claimed).ToImmutableArray() };
    }
    public bool CanAdvance(SelectedSessionIdentity session) => Completion is null && Room.State.Phase == TowerRoomPhase.Cleared
        && Members.Any(x => x.Session == session && x.IsLeader);
    public TowerActionResult NextLevel(TowerRoomSeed seed)
    {
        if (seed.Level != Room.State.Level + 1) throw new InvalidDataException("Tower room sequence does not match.");
        SetRoom(seed, Room.State.Version + 1);
        return Result(true);
    }
    public TowerActionResult Die(SelectedSessionIdentity session, DateTimeOffset now)
    {
        var member = Members.First(x => x.Session == session);
        if (Completion is not null || Room.State.Phase != TowerRoomPhase.Fighting || member.Dead) return Result(false);
        Members = Members.Replace(member, member with { Dead = true });
        if (Members.All(x => x.Dead)) End(TowerEndReason.Defeated, now);
        return Result(true);
    }
    public TowerActionResult Leave(SelectedSessionIdentity session, bool disconnected, DateTimeOffset now)
    {
        Members = Members.Where(x => x.Session != session).ToImmutableArray();
        Room.RemoveMember(session.CharacterId, Members);
        if (Members.IsEmpty) End(disconnected ? TowerEndReason.Disconnected : TowerEndReason.Abandoned, now);
        else
        {
            if (!Members.Any(x => x.IsLeader)) Members = Members.SetItem(0, Members[0] with { IsLeader = true });
            if (Members.All(x => x.Dead)) End(TowerEndReason.Defeated, now);
        }
        return Result(true);
    }
    public TowerActionResult SynchronizeLeader(SelectedSessionIdentity leader)
    {
        if (Completion is not null || !Members.Any(member => member.Session == leader))
            return Result(false);
        if (Members.Any(member => member.Session == leader && member.IsLeader))
            return Result(false);
        Members = Members
            .Select(member => member with { IsLeader = member.Session == leader })
            .ToImmutableArray();
        return Result(true);
    }
    public TowerActionResult Settle(TowerSettlementReceipt receipt, DateTimeOffset now, TimeSpan defaultReturnDelay)
    {
        if (Completion is not { Settled: false } completion) return Result(false);
        Completion = completion with { Settled = true, Receipt = receipt };
        ReturnOption = null;
        ReturnDueAt = now + defaultReturnDelay;
        return Result(true);
    }

    public TowerActionResult SelectReturn(SelectedSessionIdentity session, byte option,
        DateTimeOffset now, TimeSpan? delay)
    {
        if (Completion is not { Settled: true }
            || !Members.Any(member => member.Session == session && member.IsLeader))
            return Result(false);
        ReturnOption = option;
        ReturnDueAt = delay is null ? null : now + delay.Value;
        return Result(true);
    }
}
