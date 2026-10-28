using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class OnlineCharacterLocationDomainServiceTests
{
    [Fact]
    public void Beginning_materializes_one_area_and_query_returns_deterministic_snapshot()
    {
        TestGameplay.Run(() =>
        {
            var service = new OnlineCharacterLocationDomainService();
            var key = new TownAreaKey(7, 3, 4);

            Begin(service, Member(20, 1, key, x: 200));
            Begin(service, Member(10, 2, key, x: 100));

            var snapshot = Assert.Single(service.Query(new TownAreaFilter(7, 3, 4)));
            Assert.Equal(key, snapshot.Key);
            Assert.Equal(2, snapshot.MembershipRevision);
            Assert.Equal([10, 20], snapshot.Members.Select(value => value.CharacterId.Value));
        });
    }

    [Fact]
    public void A_replaced_session_cannot_move_or_remove_its_replacement()
    {
        TestGameplay.Run(() =>
        {
            var service = new OnlineCharacterLocationDomainService();
            var key = new TownAreaKey(7, 3, 4);
            Begin(service, Member(10, 1, key, x: 100));
            Begin(service, Member(10, 2, key, x: 200));

            Assert.Null(service.Step(Session(10, 1), Position(key, 300)));
            Assert.Null(service.Depart(Session(10, 1), TownAreaDepartureReason.DungeonEntry));
            Assert.False(service.End(Session(10, 1)));

            var current = Assert.IsType<TownAreaMember>(service.Find(new CharacterId(10)));
            Assert.Equal(Session(10, 2), current.Session);
            Assert.Equal(200, current.Position.X);
        });
    }

    [Fact]
    public void Reentering_the_identical_membership_is_idempotent()
    {
        TestGameplay.Run(() =>
        {
            var service = new OnlineCharacterLocationDomainService();
            var key = new TownAreaKey(7, 3, 4);
            var member = Member(10, 1, key, x: 100);
            var first = Begin(service, member);

            var repeated = Begin(service, member);

            Assert.Equal(TownAreaMembershipChangeKind.Unchanged, repeated.Kind);
            Assert.Equal(
                first.CurrentArea!.MembershipRevision,
                repeated.CurrentArea!.MembershipRevision);
            Assert.Single(repeated.CurrentArea.Members);
        });
    }

    [Fact]
    public void A_step_preserves_membership()
    {
        TestGameplay.Run(() =>
        {
            var service = new OnlineCharacterLocationDomainService();
            var key = new TownAreaKey(7, 3, 4);
            Begin(service, Member(10, 1, key, x: 100));
            Begin(service, Member(20, 1, key, x: 200));
            var before = Assert.IsType<TownAreaSnapshot>(service.Find(key));

            var movement = Assert.IsType<TownAreaMovement>(service.Step(Session(10, 1), Position(key, 150)));

            Assert.Equal(before.MembershipRevision, movement.Area.MembershipRevision);
            Assert.Equal(2, movement.Area.Members.Count);
            Assert.Equal<short>(150, service.Find(new CharacterId(10))!.Position.X);
        });
    }

    [Fact]
    public void Transfer_returns_committed_old_and_new_snapshots()
    {
        TestGameplay.Run(() =>
        {
            var service = new OnlineCharacterLocationDomainService();
            var oldKey = new TownAreaKey(7, 3, 4);
            var newKey = new TownAreaKey(7, 3, 5);
            Begin(service, Member(10, 1, oldKey, x: 100));
            Begin(service, Member(20, 1, oldKey, x: 200));

            var changed = Assert.IsType<TownAreaMembershipChange>(service.Transfer(
                Session(10, 1),
                newKey,
                Position(newKey, 300),
                sharesTownPresence: true));

            Assert.Equal(TownAreaMembershipChangeKind.Transferred, changed.Kind);
            Assert.Equal([20], changed.PreviousArea!.Members.Select(value => value.CharacterId.Value));
            Assert.Equal([10], changed.CurrentArea!.Members.Select(value => value.CharacterId.Value));
            Assert.Equal(newKey, service.Find(new CharacterId(10))!.Area);
        });
    }

    [Fact]
    public void Private_gate_member_can_only_see_self()
    {
        TestGameplay.Run(() =>
        {
            var service = new OnlineCharacterLocationDomainService();
            var key = new TownAreaKey(7, 3, 4);
            Begin(service, Member(10, 1, key, x: 100));
            Begin(service, Member(20, 1, key, x: 200, shares: false));

            var snapshot = Assert.IsType<TownAreaSnapshot>(service.Find(key));

            Assert.Equal([10], snapshot.VisibleTo(new CharacterId(10))
                .Select(value => value.CharacterId.Value));
            Assert.Equal([20], snapshot.VisibleTo(new CharacterId(20))
                .Select(value => value.CharacterId.Value));
        });
    }

    [Fact]
    public void Last_departure_removes_lazy_area_but_keeps_the_place_left()
    {
        TestGameplay.Run(() =>
        {
            var service = new OnlineCharacterLocationDomainService();
            var key = new TownAreaKey(7, 3, 4);
            Begin(service, Member(10, 1, key, x: 100));

            var change = Assert.IsType<TownAreaMembershipChange>(service.Depart(
                Session(10, 1), TownAreaDepartureReason.DungeonSelection));

            Assert.Empty(change.PreviousArea!.Members);
            Assert.Null(service.Find(key));
            Assert.Null(service.Find(new CharacterId(10)));
            Assert.Null(service.StandingOf(Session(10, 1)));
            Assert.Equal(Position(key, 100), service.PositionOf(new CharacterId(10)));
        });
    }

    [Fact]
    public void Replacement_moves_membership_between_areas_and_departure_is_exact()
    {
        TestGameplay.Run(() =>
        {
            var service = new OnlineCharacterLocationDomainService();
            var oldKey = new TownAreaKey(7, 3, 4);
            var newKey = new TownAreaKey(7, 3, 5);
            Begin(service, Member(10, 1, oldKey, x: 100));
            Begin(service, Member(10, 2, newKey, x: 200));

            Assert.Null(service.Find(oldKey));
            Assert.Equal(newKey, service.Find(new CharacterId(10))!.Area);
            Assert.Single(service.Query(new TownAreaFilter(7, 3, 5)));
            Assert.Null(service.Transfer(Session(10, 1), oldKey, Position(oldKey, 300), sharesTownPresence: true));
            Assert.NotNull(service.Transfer(Session(10, 2), oldKey, Position(oldKey, 300), sharesTownPresence: true));
            Assert.Null(service.Find(newKey));
            Assert.True(service.End(Session(10, 2)));
            Assert.False(service.End(Session(10, 2)));
            Assert.Empty(service.Query(new TownAreaFilter(7, 3, 4)));
            Assert.Null(service.PositionOf(new CharacterId(10)));
        });
    }

    /// <summary>
    /// Only the online character runtime writes positions; everything else reads areas through the
    /// read-only contract, so no other production type may take the location service itself.
    /// </summary>
    [Fact]
    public void Only_the_online_runtime_takes_the_location_service()
    {
        var takers = typeof(OnlineCharacterLocationDomainService).Assembly.GetTypes()
            .Where(type => type.GetConstructors(System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Any(constructor => constructor.GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(OnlineCharacterLocationDomainService))))
            .Select(type => type.Name);

        Assert.Equal(["OnlineCharacterRuntimeDomainService"], takers);
    }

    /// <summary>The store holds the position and asks nobody else.</summary>
    /// <remarks>
    /// The party reads where its members stand from here, and the party reaches the online runtime,
    /// which wraps this store: anything this store depended on would close that loop.
    /// </remarks>
    [Fact]
    public void The_location_store_depends_on_nothing() =>
        Assert.All(
            typeof(OnlineCharacterLocationDomainService).GetConstructors(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic),
            constructor => Assert.Empty(constructor.GetParameters()));

    private static TownAreaMember Member(
        int characterId,
        long generation,
        TownAreaKey key,
        short x,
        bool shares = true) =>
        new(Session(characterId, generation), key, Position(key, x), shares);

    private static TownAreaMembershipChange Begin(OnlineCharacterLocationDomainService service, TownAreaMember member) =>
        service.Begin(member.Session, member.Area, member.Position, member.SharesTownPresence);

    private static SelectedSessionIdentity Session(int characterId, long generation) =>
        SelectedSessionIdentity.Create(
            new AccountId(characterId),
            new CharacterId(characterId),
            new NetworkConnectionId($"Game:{characterId}:{generation}"),
            generation,
            listenerPort: 10011);

    private static CharacterPositionState Position(TownAreaKey key, short x) =>
        new(key.TownId, key.AreaId, x, 50, 1);
}
