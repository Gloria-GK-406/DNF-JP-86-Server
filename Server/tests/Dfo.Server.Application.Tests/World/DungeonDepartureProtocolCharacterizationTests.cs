using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// Issue #202 characterization: what each way of leaving or ending an ordinary run puts on the wire, and who is
/// left in the party, the run and the delivery registry, pinned on the baseline through the production container
/// (real party, run, delivery registry, dispatcher, outbox and encoders) before the run stops keeping a roster of
/// its own. Frames are pinned by type and hand-off order across both connections; their bodies have tests of their
/// own. Assertions read only the run views, lookups, party snapshots and the delivery registry.
/// </summary>
public sealed class DungeonDepartureProtocolCharacterizationTests
{
    private const ushort UserState = 0x03, PartyRoster = 0x09, PartyEndpoints = 0x0B, UserArea = 0x17, AreaUsers = 0x18,
        UdpHost = 0x1A, Weakness = 0x21, GiveUp = 0x2A, Eplp = 0x48, BackToVillage = 0x84, PartyRealtime = 0x99;
    private const ushort LeaveParty = 0x0D, RemovePartyMember = 0x0E, ChangePartyLeader = 0x79;

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Giving_up_detaches_the_giver_then_returns_them_to_town_and_the_other_member_keeps_the_run_and_leads(bool leaderGivesUp)
    {
        using var run = await TwoMemberRun.EnterAsync();
        var (departing, remaining) = run.Split(leaderGivesUp);

        await run.WireAsync(departing, GiveUp, []);

        Assert.Equal(
            [
                (departing, PartyRoster),
                .. PartyUpdate(remaining, hostChanged: leaderGivesUp),
                (departing, GiveUp), (departing, Weakness), (departing, UserState), (departing, UserArea), (departing, AreaUsers),
            ],
            run.Sequence());
        run.AssertDetached(departing, remaining);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Disconnecting_detaches_the_character_and_only_the_other_member_is_told(bool leaderDisconnects)
    {
        using var run = await TwoMemberRun.EnterAsync();
        var (departing, remaining) = run.Split(leaderDisconnects);

        await run.F.Services.GetRequiredService<INetworkConnectionLifecycleDispatcher>()
            .DispatchDisconnectedAsync(run.Connection(departing), default);

        Assert.Equal(PartyUpdate(remaining, hostChanged: leaderDisconnects), run.Sequence());
        TestGameplay.Run(run.F.Services, () =>
            Assert.False(run.F.Services.GetRequiredService<ISelectedSessionDomainService>().IsInWorld(run.Session(departing))));
        run.AssertDetached(departing, remaining);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Leaving_the_party_in_a_run_detaches_the_leaver_without_a_town_return(bool leaderLeaves)
    {
        using var run = await TwoMemberRun.EnterAsync();
        var (departing, remaining) = run.Split(leaderLeaves);

        await run.WireAsync(departing, LeaveParty, []);

        Assert.Equal(
            [(departing, PartyRoster), .. PartyUpdate(remaining, hostChanged: leaderLeaves)],
            run.Sequence());
        run.AssertDetached(departing, remaining);
    }

    [Fact]
    public async Task Removing_the_member_in_a_run_detaches_them_then_returns_them_to_town()
    {
        using var run = await TwoMemberRun.EnterAsync();

        await run.WireAsync(Side.Leader, RemovePartyMember, [1]);

        Assert.Equal(
            [
                (Side.Member, PartyRoster),
                .. PartyUpdate(Side.Leader, hostChanged: false),
                (Side.Member, UserState), (Side.Member, UserArea), (Side.Member, AreaUsers), (Side.Member, Weakness),
            ],
            run.Sequence());
        run.AssertDetached(Side.Member, Side.Leader);
    }

    [Fact]
    public async Task Transferring_leadership_in_a_run_keeps_both_members_and_moves_the_run_leader()
    {
        using var run = await TwoMemberRun.EnterAsync();

        await run.WireAsync(Side.Leader, ChangePartyLeader, [1]);

        Assert.Equal(
            [(Side.Leader, UdpHost), (Side.Leader, PartyRoster), (Side.Member, UdpHost), (Side.Member, PartyRoster)],
            run.Sequence());
        Assert.Empty(run.F.Sender.Outbound.Closes);
        TestGameplay.Run(run.F.Services, () =>
        {
            var runs = run.F.Services.GetRequiredService<IDungeonRunDomainService>();
            foreach (var side in new[] { Side.Leader, Side.Member })
            {
                var view = Assert.IsType<DungeonRunState>(runs.Find(run.Session(side).CharacterId));
                Assert.Equal(run.RunId, view.RunId);
                Assert.Equal(run.Member.CharacterId, Assert.Single(view.Participants, value => value.IsLeader).CharacterId);
                Assert.Equal([run.Leader.CharacterId, run.Member.CharacterId], view.Participants.Select(value => value.CharacterId).OrderBy(value => value.Value));
            }
            Assert.Equal([run.Leader, run.Member], run.Deliveries.Find(run.RunId));
            run.AssertParty(leader: run.Member, run.Leader, run.Member);
        });
    }

    [Fact]
    public async Task Eplp_by_the_leader_ends_the_run_for_both_with_the_peer_decision_before_the_leader_ack()
    {
        using var run = await TwoMemberRun.EnterAsync();

        await run.WireAsync(Side.Leader, Eplp, [1, 2]);

        Assert.Equal(
            [
                (Side.Member, Eplp), (Side.Leader, Eplp),
                (Side.Leader, UserState), (Side.Leader, UserArea), (Side.Leader, AreaUsers), (Side.Leader, Weakness),
                (Side.Member, UserState), (Side.Member, UserArea), (Side.Member, AreaUsers), (Side.Member, Weakness),
                (Side.Leader, PartyRoster), (Side.Member, PartyRoster), (Side.Leader, PartyRoster), (Side.Member, PartyRoster),
            ],
            run.Sequence());
        run.AssertEndedForBoth();
    }

    [Fact]
    public async Task Back_to_village_by_the_leader_with_a_run_ends_it_for_both_and_keeps_the_party()
    {
        // Not reachable today (an entry intent and a run are not held together, Spec "可达性说明"); the defensive
        // branch is pinned so the restructure keeps what it sends when the leader does reach it.
        using var run = await TwoMemberRun.EnterAsync();
        TestGameplay.Run(run.F.Services, () =>
        {
            var intents = run.F.Services.GetRequiredService<IDungeonEntryIntentDomainService>();
            intents.Record(run.Leader, DungeonEntryIntent.Interactive);
            intents.Record(run.Member, DungeonEntryIntent.Interactive);
        });

        await run.WireAsync(Side.Leader, BackToVillage, []);

        Assert.Equal(
            [
                (Side.Leader, BackToVillage),
                (Side.Leader, UserState), (Side.Leader, UserArea), (Side.Leader, AreaUsers), (Side.Leader, Weakness),
                (Side.Member, UserState), (Side.Member, UserArea), (Side.Member, AreaUsers), (Side.Member, Weakness),
                (Side.Leader, PartyRoster), (Side.Member, PartyRoster), (Side.Leader, PartyRoster), (Side.Member, PartyRoster),
            ],
            run.Sequence());
        run.AssertEndedForBoth();
    }

    [Fact]
    public async Task Disconnecting_alone_ends_the_whole_run_and_sends_nothing()
    {
        // The second character is online but in no party and no run: nothing may reach it.
        using var run = await TwoMemberRun.EnterAsync(party: false);

        await run.F.Services.GetRequiredService<INetworkConnectionLifecycleDispatcher>()
            .DispatchDisconnectedAsync(run.LeaderConnection, default);

        Assert.Empty(run.Sequence());
        Assert.Empty(run.F.Sender.Outbound.Closes);
        TestGameplay.Run(run.F.Services, () =>
        {
            var runs = run.F.Services.GetRequiredService<IDungeonRunDomainService>();
            Assert.Null(runs.Find(run.RunId));
            Assert.Null(runs.Find(run.Leader.CharacterId));
            Assert.Empty(run.Deliveries.Find(run.RunId));
        });
    }

    /// <summary>
    /// What the remaining member is sent when the other leaves the party: realtime, endpoints and roster, preceded by
    /// a host-change roster when the leader was the one who left.
    /// </summary>
    private static (Side, ushort)[] PartyUpdate(Side remaining, bool hostChanged) =>
        [.. hostChanged ? new[] { (remaining, PartyRoster) } : [], (remaining, PartyRealtime), (remaining, PartyEndpoints), (remaining, PartyRoster)];

    internal enum Side { Leader, Member }

    /// <summary>
    /// A two-character real party (leader = the fixture's session, member = a second online character) in one
    /// ordinary run, entered with <see cref="CharacterDeathProtocolLifecycleTests.Enter"/>: the run, the party and
    /// the delivery registration are real; neither character stands in a town area, as after a real entry.
    /// </summary>
    internal sealed class TwoMemberRun : IDisposable
    {
        private TwoMemberRun(ShopFixture f, SelectedSessionIdentity member, NetworkConnectionContext memberConnection, DungeonRunId runId)
        { F = f; Member = member; MemberConnection = memberConnection; RunId = runId; }

        public ShopFixture F { get; }
        public SelectedSessionIdentity Leader => F.Session;
        public SelectedSessionIdentity Member { get; }
        public NetworkConnectionContext LeaderConnection => F.Connection;
        public NetworkConnectionContext MemberConnection { get; }
        public DungeonRunId RunId { get; }
        public DungeonRunDeliveryRegistry Deliveries => F.Services.GetRequiredService<DungeonRunDeliveryRegistry>();

        /// <param name="party">False enters the leader alone (a virtual party); the second character stays online outside it.</param>
        public static async Task<TwoMemberRun> EnterAsync(bool party = true)
        {
            var f = new ShopFixture(s => s.AddSingleton<ISelectedCharacterCurrentViewDomainService, EntryFailureProbe.CompleteView>());
            SelectedSessionIdentity member = null!;
            NetworkConnectionContext connection = null!;
            await TestGameplay.RunAsync(f.Services, async () =>
            {
                member = await BossTowerDomainServiceTests.AddMember(f, 0);
                f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().Begin(member, new(0, 0, 0, 0, 0), 100);
                Assert.True(f.Services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(member.ConnectionId, out var user));
                connection = user!.Connection;
            });
            f.Sender.Outbound.Connections.Add(connection);
            var run = CharacterDeathProtocolLifecycleTests.Enter(f, party ? member : null);
            TestGameplay.Run(f.Services, () => Assert.NotNull(f.Services.GetRequiredService<ITownAreaDomainService>()
                .LeaveExact(f.Session.CharacterId, f.Session.TownAreaLease(), TownAreaDepartureReason.DungeonSelection)));
            f.Sender.Outbound.Clear();
            return new TwoMemberRun(f, member, connection, run);
        }

        public (Side Departing, Side Remaining) Split(bool leaderDeparts) =>
            leaderDeparts ? (Side.Leader, Side.Member) : (Side.Member, Side.Leader);

        public SelectedSessionIdentity Session(Side side) => side == Side.Leader ? Leader : Member;

        public NetworkConnectionContext Connection(Side side) => side == Side.Leader ? LeaderConnection : MemberConnection;

        public ValueTask WireAsync(Side side, ushort type, byte[] body) =>
            DungeonExitLifecycleTests.WireOn(F, Connection(side), type, body);

        /// <summary>Every registered frame's type in hand-off order, with the side it was addressed to.</summary>
        public IReadOnlyList<(Side Side, ushort Type)> Sequence() =>
            [.. F.Sender.Outbound.Entries.Where(entry => !entry.IsClose)
                .SelectMany(entry => F.Sender.Outbound.FramesOf(entry).SelectMany(CharacterDeathOracle.Types)
                    .Select(type => (SideOf(entry.Connection.ConnectionId), type)))];

        private Side SideOf(NetworkConnectionId id) =>
            id == LeaderConnection.ConnectionId ? Side.Leader
            : id == MemberConnection.ConnectionId ? Side.Member
            : throw new InvalidOperationException($"A frame reached an unexpected connection {id}.");

        /// <summary>The departing character is out of the run, the party and the registry; the other keeps the run and leads it.</summary>
        public void AssertDetached(Side departing, Side remaining)
        {
            Assert.Empty(F.Sender.Outbound.Closes);
            TestGameplay.Run(F.Services, () =>
            {
                var runs = F.Services.GetRequiredService<IDungeonRunDomainService>();
                Assert.Null(runs.Find(Session(departing).CharacterId));
                var view = Assert.IsType<DungeonRunState>(runs.Find(Session(remaining).CharacterId));
                Assert.Equal(RunId, view.RunId);
                var only = Assert.Single(view.Participants);
                Assert.Equal(Session(remaining).CharacterId, only.CharacterId);
                Assert.True(only.IsLeader);
                Assert.Equal([Session(remaining)], Deliveries.Find(RunId));
                AssertParty(leader: Session(remaining), Session(remaining));
                Assert.True(F.Services.GetRequiredService<IPartyService>().FindParty(Snapshot(Session(departing))).IsVirtual);
            });
        }

        /// <summary>The run is gone for both and the registry with it; the party is untouched.</summary>
        public void AssertEndedForBoth()
        {
            Assert.Empty(F.Sender.Outbound.Closes);
            TestGameplay.Run(F.Services, () =>
            {
                var runs = F.Services.GetRequiredService<IDungeonRunDomainService>();
                Assert.Null(runs.Find(RunId));
                Assert.Null(runs.Find(Leader.CharacterId));
                Assert.Null(runs.Find(Member.CharacterId));
                Assert.Empty(Deliveries.Find(RunId));
                AssertParty(leader: Leader, Leader, Member);
            });
        }

        public void AssertParty(SelectedSessionIdentity leader, params SelectedSessionIdentity[] members)
        {
            var party = F.Services.GetRequiredService<IPartyService>().FindParty(Snapshot(leader));
            Assert.False(party.IsVirtual);
            Assert.Equal(leader.WireUserId, party.LeaderWireUserId);
            Assert.Equal(members.Select(value => value.CharacterId), party.Members.OrderBy(value => value.Slot).Select(value => value.CharacterId));
        }

        private static PartyCharacterSnapshot Snapshot(SelectedSessionIdentity session) =>
            new(session.AccountId, session.CharacterId, session.WireUserId, null);

        public void Dispose() => F.Dispose();
    }
}
