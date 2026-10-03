using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.Tests.World;

public sealed class TownAreaDomainServiceTests
{
    [Fact]
    public void Enter_materializes_one_area_and_query_returns_deterministic_snapshot()
    {
        var service = new TownAreaDomainService();
        var key = new TownAreaKey(7, 3, 4);

        service.Enter(Member(20, 1, key, x: 200));
        service.Enter(Member(10, 2, key, x: 100));

        var snapshot = Assert.Single(service.Query(new TownAreaFilter(7, 3, 4)));
        Assert.Equal(key, snapshot.Key);
        Assert.Equal(2, snapshot.MembershipRevision);
        Assert.Equal([10, 20], snapshot.Members.Select(value => value.CharacterId.Value));
    }

    [Fact]
    public void Stale_lease_cannot_move_or_remove_replacement()
    {
        var service = new TownAreaDomainService();
        var key = new TownAreaKey(7, 3, 4);
        service.Enter(Member(10, 1, key, x: 100));
        service.Enter(Member(10, 2, key, x: 200));

        Assert.Null(service.MoveExact(
            new CharacterId(10),
            new TownAreaMembershipLease(1),
            Position(key, 300)));
        Assert.Null(service.LeaveExact(
            new CharacterId(10),
            new TownAreaMembershipLease(1),
            TownAreaDepartureReason.SessionEnded));

        var current = Assert.IsType<TownAreaMember>(service.Find(new CharacterId(10)));
        Assert.Equal(2, current.Lease.Value);
        Assert.Equal(200, current.Position.X);
    }

    [Fact]
    public void Reentering_the_identical_membership_is_idempotent()
    {
        var party = new RecordingPartyPresence();
        var service = new TownAreaDomainService(party);
        var key = new TownAreaKey(7, 3, 4);
        var member = Member(10, 1, key, x: 100);
        var first = service.Enter(member);

        var repeated = service.Enter(member);

        Assert.Equal(TownAreaMembershipChangeKind.Unchanged, repeated.Kind);
        Assert.Equal(
            first.CurrentArea!.MembershipRevision,
            repeated.CurrentArea!.MembershipRevision);
        Assert.Single(repeated.CurrentArea.Members);
        Assert.Single(party.Updates);
    }

    [Fact]
    public void Move_changes_only_movement_revision_and_preserves_membership()
    {
        var service = new TownAreaDomainService();
        var key = new TownAreaKey(7, 3, 4);
        service.Enter(Member(10, 1, key, x: 100));
        service.Enter(Member(20, 1, key, x: 200));
        var before = Assert.IsType<TownAreaSnapshot>(service.Find(key));

        var movement = Assert.IsType<TownAreaMovement>(service.MoveExact(
            new CharacterId(10),
            new TownAreaMembershipLease(1),
            Position(key, 150)));

        Assert.Equal(before.MembershipRevision, movement.Area.MembershipRevision);
        Assert.Equal(before.MovementRevision + 1, movement.Area.MovementRevision);
        Assert.Equal(2, movement.Area.Members.Count);
        Assert.Equal<short>(150, service.Find(new CharacterId(10))!.Position.X);
    }

    [Fact]
    public void Transfer_returns_committed_old_and_new_snapshots_and_updates_party_scope()
    {
        var party = new RecordingPartyPresence();
        var service = new TownAreaDomainService(party);
        var oldKey = new TownAreaKey(7, 3, 4);
        var newKey = new TownAreaKey(7, 3, 5);
        service.Enter(Member(10, 1, oldKey, x: 100));
        service.Enter(Member(20, 1, oldKey, x: 200));

        var changed = Assert.IsType<TownAreaMembershipChange>(service.TransferExact(
            new CharacterId(10),
            new TownAreaMembershipLease(1),
            newKey,
            Position(newKey, 300),
            sharesTownPresence: true));

        Assert.Equal(TownAreaMembershipChangeKind.Transferred, changed.Kind);
        Assert.Equal([20], changed.PreviousArea!.Members.Select(value => value.CharacterId.Value));
        Assert.Equal([10], changed.CurrentArea!.Members.Select(value => value.CharacterId.Value));
        Assert.Equal(new PartyDirectoryScope(7, 3, 5), party.Updates[^1].Scope);
    }

    [Fact]
    public void Private_gate_member_can_only_see_self()
    {
        var service = new TownAreaDomainService();
        var key = new TownAreaKey(7, 3, 4);
        service.Enter(Member(10, 1, key, x: 100));
        service.Enter(Member(20, 1, key, x: 200, shares: false));

        var snapshot = Assert.IsType<TownAreaSnapshot>(service.Find(key));

        Assert.Equal([10], snapshot.VisibleTo(new CharacterId(10))
            .Select(value => value.CharacterId.Value));
        Assert.Equal([20], snapshot.VisibleTo(new CharacterId(20))
            .Select(value => value.CharacterId.Value));
    }

    [Fact]
    public void Last_departure_removes_lazy_area_and_clears_party_scope()
    {
        var party = new RecordingPartyPresence();
        var service = new TownAreaDomainService(party);
        var key = new TownAreaKey(7, 3, 4);
        service.Enter(Member(10, 1, key, x: 100));

        var change = Assert.IsType<TownAreaMembershipChange>(service.LeaveExact(
            new CharacterId(10),
            new TownAreaMembershipLease(1),
            TownAreaDepartureReason.DungeonSelection));

        Assert.Empty(change.PreviousArea!.Members);
        Assert.Null(service.Find(key));
        Assert.Null(service.Find(new CharacterId(10)));
        Assert.Null(party.Updates[^1].Scope);
    }

    [Fact]
    public void Replacement_moves_membership_between_areas_and_departure_is_exact()
    {
        var service = new TownAreaDomainService();
        var oldKey = new TownAreaKey(7, 3, 4);
        var newKey = new TownAreaKey(7, 3, 5);
        service.Enter(Member(10, 1, oldKey, x: 100));
        service.Enter(Member(10, 2, newKey, x: 200));

        Assert.Null(service.Find(oldKey));
        Assert.Equal(newKey, service.Find(new CharacterId(10))!.Area);
        Assert.Single(service.Query(new TownAreaFilter(7, 3, 5)));
        Assert.Null(service.TransferExact(new CharacterId(10), new TownAreaMembershipLease(1),
            oldKey, Position(oldKey, 300), sharesTownPresence: true));
        Assert.NotNull(service.TransferExact(new CharacterId(10), new TownAreaMembershipLease(2),
            oldKey, Position(oldKey, 300), sharesTownPresence: true));
        Assert.Null(service.Find(newKey));
        Assert.NotNull(service.LeaveExact(new CharacterId(10), new TownAreaMembershipLease(2),
            TownAreaDepartureReason.SessionEnded));
        Assert.Null(service.LeaveExact(new CharacterId(10), new TownAreaMembershipLease(2),
            TownAreaDepartureReason.SessionEnded));
        Assert.Empty(service.Query(new TownAreaFilter(7, 3, 4)));
    }

    private static TownAreaMember Member(
        int characterId,
        long lease,
        TownAreaKey key,
        short x,
        bool shares = true) =>
        new(
            new CharacterId(characterId),
            new TownAreaMembershipLease(lease),
            key,
            Position(key, x),
            shares);

    private static CharacterPositionState Position(TownAreaKey key, short x) =>
        new(key.TownId, key.AreaId, x, 50, 1);

    private sealed class RecordingPartyPresence : IPartyDirectoryPresenceSink
    {
        public List<(CharacterId CharacterId, PartyDirectoryScope? Scope)> Updates { get; } = [];

        public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope) =>
            Updates.Add((characterId, scope));
    }
}
