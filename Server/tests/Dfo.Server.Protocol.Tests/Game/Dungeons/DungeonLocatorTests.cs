using System.Collections.Immutable;
using System.Reflection;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

/// <summary>
/// The place a requester stands is the occupancy registry's answer, confirmed by the registered
/// tower for this exact session and run; every other combination is ordinary or none.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DungeonLocatorTests
{
    private readonly Registry registry = new();
    private readonly IBossTowerDomainService boss;
    private readonly IIllusionTowerDomainService illusion;
    private readonly Seats bossSeats;
    private readonly Seats illusionSeats;
    private int nextCharacter = 1;

    public DungeonLocatorTests()
    {
        boss = Seats.Create<IBossTowerDomainService>(out bossSeats);
        illusion = Seats.Create<IIllusionTowerDomainService>(out illusionSeats);
    }

    [Fact]
    public void Unregistered_session_is_ordinary_or_none_without_asking_a_tower()
    {
        var session = Session();

        var place = Locate(session);

        Assert.Equal(new DungeonPlace.OrdinaryOrNone(session), place);
        AssertNoTowerAsked();
    }

    [Fact]
    public void Ordinary_registration_is_ordinary_or_none_without_asking_a_tower()
    {
        var session = Session();
        registry.Owners[session.CharacterId] = new(DungeonParticipationMode.Ordinary, Guid.NewGuid());

        Assert.Equal(new DungeonPlace.OrdinaryOrNone(session), Locate(session));
        AssertNoTowerAsked();
    }

    [Fact]
    public void Ordinary_registration_whose_run_is_no_longer_found_is_still_ordinary_or_none()
    {
        // After a party departure that has not yet ended the character's part, the registry
        // still names an ordinary run the run service no longer finds by character.
        var session = Session();
        registry.Owners[session.CharacterId] = new(DungeonParticipationMode.Ordinary, Guid.NewGuid());
        bossSeats.Seat(BossRun(session));
        illusionSeats.Seat(IllusionRun(session));

        Assert.Equal(new DungeonPlace.OrdinaryOrNone(session), Locate(session));
        AssertNoTowerAsked();
    }

    [Fact]
    public void Registered_boss_tower_seating_this_session_is_the_boss_tower()
    {
        var session = Session();
        var run = RegisterBoss(session);

        var place = Assert.IsType<DungeonPlace.InBossTower>(Locate(session));

        Assert.Same(run, place.Run);
        Assert.Equal(session, place.Session);
        Assert.Equal([session], bossSeats.Asked);
        Assert.Empty(illusionSeats.Asked);
    }

    [Fact]
    public void Registered_illusion_tower_seating_this_session_is_the_illusion_tower()
    {
        var session = Session();
        var run = RegisterIllusion(session);

        var place = Assert.IsType<DungeonPlace.InIllusionTower>(Locate(session));

        Assert.Same(run, place.Run);
        Assert.Equal(session, place.Session);
        Assert.Equal([session], illusionSeats.Asked);
        Assert.Empty(bossSeats.Asked);
    }

    [Theory]
    [InlineData(DungeonParticipationMode.BossTower)]
    [InlineData(DungeonParticipationMode.IllusionTower)]
    public void Tower_seat_held_by_an_older_generation_is_ordinary_or_none(DungeonParticipationMode mode)
    {
        var older = Session();
        var current = older with { ConnectionGeneration = older.ConnectionGeneration + 1 };
        if (mode == DungeonParticipationMode.BossTower) RegisterBoss(older);
        else RegisterIllusion(older);

        Assert.Equal(new DungeonPlace.OrdinaryOrNone(current), Locate(current));
    }

    [Theory]
    [InlineData(DungeonParticipationMode.BossTower)]
    [InlineData(DungeonParticipationMode.IllusionTower)]
    public void Tower_run_other_than_the_registered_one_is_ordinary_or_none(DungeonParticipationMode mode)
    {
        var session = Session();
        if (mode == DungeonParticipationMode.BossTower) bossSeats.Seat(BossRun(session));
        else illusionSeats.Seat(IllusionRun(session));
        registry.Owners[session.CharacterId] = new(mode, Guid.NewGuid());

        Assert.Equal(new DungeonPlace.OrdinaryOrNone(session), Locate(session));
    }

    [Fact]
    public void Only_the_registered_tower_is_asked()
    {
        // The illusion tower seats the session, but the registry names the Boss tower.
        var session = Session();
        var illusionRun = IllusionRun(session);
        illusionSeats.Seat(illusionRun);
        registry.Owners[session.CharacterId] = new(DungeonParticipationMode.BossTower, illusionRun.RunId);

        Assert.Equal(new DungeonPlace.OrdinaryOrNone(session), Locate(session));
        Assert.Empty(illusionSeats.Asked);
    }

    [Fact]
    public void Party_is_in_the_boss_tower_when_only_a_member_is()
    {
        var caller = Session();
        var member = Session();
        var run = RegisterBoss(member);

        var place = Assert.IsType<DungeonPartyPlace.PartyInBossTower>(LocateParty(caller, member));

        Assert.Equal([run], place.Runs);
    }

    [Fact]
    public void Party_prefers_the_boss_tower_over_the_illusion_tower()
    {
        var illusionMember = Session();
        var bossMember = Session();
        RegisterIllusion(illusionMember);
        var run = RegisterBoss(bossMember);

        var place = Assert.IsType<DungeonPartyPlace.PartyInBossTower>(LocateParty(illusionMember, bossMember));

        Assert.Equal([run], place.Runs);
    }

    [Fact]
    public void Party_is_in_the_illusion_tower_when_no_candidate_is_in_the_boss_tower()
    {
        var caller = Session();
        var ordinary = Session();
        var member = Session();
        registry.Owners[ordinary.CharacterId] = new(DungeonParticipationMode.Ordinary, Guid.NewGuid());
        var run = RegisterIllusion(member);

        var place = Assert.IsType<DungeonPartyPlace.PartyInIllusionTower>(LocateParty(caller, ordinary, member));

        Assert.Equal([run], place.Runs);
    }

    [Fact]
    public void Party_carries_every_distinct_boss_run_in_candidate_order()
    {
        var first = Session();
        var firstMate = Session();
        var second = Session();
        var town = Session();
        var secondRun = RegisterBoss(second);
        var firstRun = RegisterBoss(first, firstMate);

        var place = Assert.IsType<DungeonPartyPlace.PartyInBossTower>(LocateParty(town, first, second, firstMate, first));

        Assert.Equal([firstRun, secondRun], place.Runs);
        Assert.Equal(1, bossSeats.Asked.Count(session => session == first));
    }

    [Fact]
    public void Party_carries_every_distinct_illusion_run_in_candidate_order()
    {
        var first = Session();
        var second = Session();
        var secondMate = Session();
        var firstRun = RegisterIllusion(first);
        var secondRun = RegisterIllusion(second, secondMate);

        var place = Assert.IsType<DungeonPartyPlace.PartyInIllusionTower>(LocateParty(secondMate, first, second));

        Assert.Equal([secondRun, firstRun], place.Runs);
    }

    [Fact]
    public void Party_in_town_or_ordinary_runs_is_ordinary_or_none()
    {
        var town = Session();
        var ordinary = Session();
        registry.Owners[ordinary.CharacterId] = new(DungeonParticipationMode.Ordinary, Guid.NewGuid());

        Assert.IsType<DungeonPartyPlace.PartyOrdinaryOrNone>(LocateParty(town, ordinary));
    }

    [Fact]
    public void Party_member_whose_tower_seat_is_held_by_an_older_generation_does_not_place_the_party()
    {
        var caller = Session();
        var older = Session();
        RegisterBoss(older);
        var current = older with { ConnectionGeneration = older.ConnectionGeneration + 1 };

        Assert.IsType<DungeonPartyPlace.PartyOrdinaryOrNone>(LocateParty(caller, current));
    }

    private DungeonPlace Locate(SelectedSessionIdentity session) => DungeonLocator.Locate(session, registry, boss, illusion);

    private DungeonPartyPlace LocateParty(params SelectedSessionIdentity[] candidates) =>
        DungeonLocator.LocateParty(candidates, registry, boss, illusion);

    private void AssertNoTowerAsked()
    {
        Assert.Empty(bossSeats.Asked);
        Assert.Empty(illusionSeats.Asked);
    }

    private SelectedSessionIdentity Session()
    {
        var id = nextCharacter++;
        return new(new AccountId(id), new CharacterId(id), new NetworkConnectionId($"locator-{id}"), 1, checked((ushort)id), 10011);
    }

    private BossTowerSnapshot RegisterBoss(params SelectedSessionIdentity[] members)
    {
        var run = BossRun(members);
        bossSeats.Seat(run);
        foreach (var member in members)
            registry.Owners[member.CharacterId] = new(DungeonParticipationMode.BossTower, run.RunId);
        return run;
    }

    private IllusionTowerSnapshot RegisterIllusion(params SelectedSessionIdentity[] members)
    {
        var run = IllusionRun(members);
        illusionSeats.Seat(run);
        foreach (var member in members)
            registry.Owners[member.CharacterId] = new(DungeonParticipationMode.IllusionTower, run.RunId);
        return run;
    }

    private static BossTowerSnapshot BossRun(params SelectedSessionIdentity[] members) =>
        new(Guid.NewGuid(), 3505, Members(members), Room(), [], null, DateTimeOffset.UnixEpoch);

    private static IllusionTowerSnapshot IllusionRun(params SelectedSessionIdentity[] members) =>
        new(Guid.NewGuid(), 11001, Members(members), Room(), [], null, DateTimeOffset.UnixEpoch);

    private static ImmutableArray<TowerMember> Members(SelectedSessionIdentity[] members) =>
        [.. members.Select((member, slot) => new TowerMember(member, (byte)slot, slot == 0, false))];

    private static TowerRoomSnapshot Room() => new(1, 60001, 1, 1, TowerRoomPhase.Fighting, [], [], ImmutableHashSet<CharacterId>.Empty);

    private sealed class Registry : IDungeonParticipationDomainService
    {
        public Dictionary<CharacterId, DungeonParticipationOwner> Owners { get; } = [];
        public DungeonParticipationOwner? Find(CharacterId character) => Owners.GetValueOrDefault(character);
        public bool TryClaim(IReadOnlyList<CharacterId> characters, DungeonParticipationOwner owner) => throw new InvalidOperationException("Locating never claims.");
        public void Release(CharacterId character, DungeonParticipationOwner expected) => throw new InvalidOperationException("Locating never releases.");
        public void ReleaseCommitted(CharacterId character, DungeonParticipationOwner expected) => throw new InvalidOperationException("Locating never releases.");
    }

    /// <summary>A tower that seats runs by exact session and answers only <c>Find</c>, recording who asked.</summary>
    public class Seats : DispatchProxy
    {
        private readonly List<object> runs = [];
        public List<SelectedSessionIdentity> Asked { get; } = [];

        internal static T Create<T>(out Seats seats) where T : class
        {
            var tower = Create<T, Seats>();
            seats = (Seats)(object)tower;
            return tower;
        }

        internal void Seat(object run) => runs.Add(run);

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != "Find" || args is not [SelectedSessionIdentity session])
                throw new InvalidOperationException($"Locating never calls {method?.Name}.");
            Asked.Add(session);
            return runs.SingleOrDefault(run => MembersOf(run).Any(member => member.Session == session));
        }

        private static ImmutableArray<TowerMember> MembersOf(object run) => run switch
        {
            BossTowerSnapshot boss => boss.Members,
            IllusionTowerSnapshot illusion => illusion.Members,
            _ => throw new InvalidOperationException(run.GetType().Name),
        };
    }
}
