using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.Party;

/// <summary>
/// A party bound to a dungeon run takes no new members (Issue #202 RQ-007, AC-009). The party service
/// itself has no such rule, so every case also proves the same request succeeds without the run.
/// </summary>
/// <remarks>
/// Every character stands in town here: in the game a run member has left town and the existing town
/// check refuses first, so only these fixtures reach the run guard.
/// </remarks>
public sealed partial class PartyCommandHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inviting_a_character_into_a_party_is_refused_while_the_party_has_a_run(bool running)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = RunningPartyFixture.Create();
            fixture.FormParty();
            var run = running ? fixture.EnterPartyRun() : null;

            Assert.Equal(!running, await fixture.RequestPeerAsync(Inviter, Outsider));
            if (running)
            {
                Assert.Equal(PartyFailureReason.InviteNotFoundOrStale,
                    fixture.Parties.AcceptInvite(Snapshot(Outsider), Snapshot(Inviter)).Failure);
                // An invitation the party service holds (one sent before entry, say) is refused on acceptance.
                Assert.True(fixture.Parties.RequestInvite(Snapshot(Inviter), Snapshot(Outsider),
                    new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            }

            Assert.Equal(!running, await fixture.ResponsePeerAsync(Outsider, inviter: Inviter));
            if (running)
                fixture.AssertRunPartyUnchanged(run!);
            else
                fixture.AssertPartyMembers(Inviter, Invitee, Outsider);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Applying_to_a_party_is_refused_while_the_party_has_a_run(bool running)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = RunningPartyFixture.Create();
            fixture.FormParty();
            var run = running ? fixture.EnterPartyRun() : null;

            // The same peer request sent by a character outside a party to a leader is an application.
            Assert.Equal(!running, await fixture.RequestPeerAsync(Outsider, Inviter));
            if (running)
            {
                Assert.Equal(PartyFailureReason.InviteNotFoundOrStale,
                    fixture.Parties.AcceptInvite(Snapshot(Inviter), Snapshot(Outsider)).Failure);
                Assert.True(fixture.Parties.RequestInvite(Snapshot(Outsider), Snapshot(Inviter),
                    new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            }

            Assert.Equal(!running, await fixture.ResponsePeerAsync(Inviter, inviter: Outsider));
            if (running)
                fixture.AssertRunPartyUnchanged(run!);
            else
                fixture.AssertPartyMembers(Inviter, Invitee, Outsider);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_character_alone_in_a_run_cannot_join_a_party(bool running)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = RunningPartyFixture.Create();
            var run = running ? fixture.EnterSoloRun(Outsider) : null;

            Assert.Equal(!running, await fixture.RequestPeerAsync(Inviter, Outsider));
            if (running)
                Assert.True(fixture.Parties.RequestInvite(Snapshot(Inviter), Snapshot(Outsider),
                    new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);

            Assert.Equal(!running, await fixture.ResponsePeerAsync(Outsider, inviter: Inviter));
            if (running)
            {
                Assert.Equal(PartyKey.Virtual(Outsider.CharacterId), fixture.Parties.KeyOf(Outsider.CharacterId));
                Assert.Equal(PartyKeyKind.Virtual, fixture.Parties.KeyOf(Inviter.CharacterId).Kind);
                var view = Assert.IsType<DungeonRunState>(fixture.Runs.Find(Outsider.CharacterId));
                Assert.Equal(run!.RunId, view.RunId);
                Assert.Equal(Outsider.CharacterId, Assert.Single(view.Participants).CharacterId);
            }
            else
            {
                fixture.AssertPartyMembers(Inviter, Outsider);
            }
        });
    }

    private static SelectedSessionIdentity Outsider { get; } = SelectedSessionIdentity.Create(
        new AccountId(3), new CharacterId(3), new NetworkConnectionId("outsider"), 1, 10011);

    private static PartyCharacterSnapshot Snapshot(SelectedSessionIdentity session) => Character(session);

    private sealed class RunningPartyFixture
    {
        private static readonly DungeonLayout Layout = new(
            10000, 0, 1, 1, new MazeCell(0, 0), new MazeCell(0, 0),
            61000, 1, new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(61000) });

        private readonly SessionDirectory sessions = new(Inviter, Invitee, Outsider);
        private readonly FixedPresence town = new(Inviter, Invitee, Outsider);
        private readonly TestProjection projection = new(new NoopNotificationBus());

        private RunningPartyFixture(PartyClientProjectionService parties, DungeonRunDomainService runs)
        {
            Parties = parties;
            Runs = runs;
        }

        public PartyClientProjectionService Parties { get; }
        public DungeonRunDomainService Runs { get; }

        public static RunningPartyFixture Create()
        {
            var parties = new PartyClientProjectionService(new PartyService(
                new FixedPartyMemberStatus(), new PartyModeRuleService(),
                TimeProvider.System, new NoPartyEndpoints(), new Dfo.Server.Application.World.OnlineCharacterLocationDomainService()), new PartyClientIdRegistry());
            return new RunningPartyFixture(parties, TestParties.Runs(parties: parties));
        }

        public void FormParty()
        {
            Assert.True(Parties.RequestInvite(Snapshot(Inviter), Snapshot(Invitee),
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(Parties.AcceptInvite(Snapshot(Invitee), Snapshot(Inviter)).Succeeded);
        }

        public DungeonRunState EnterPartyRun() => Enter(
            new DungeonRunParticipant(Inviter.AccountId, Inviter.CharacterId, 0, true),
            new DungeonRunParticipant(Invitee.AccountId, Invitee.CharacterId, 1, false));

        public DungeonRunState EnterSoloRun(SelectedSessionIdentity session) =>
            Enter(new DungeonRunParticipant(session.AccountId, session.CharacterId, 0, true));

        public async Task<bool> RequestPeerAsync(SelectedSessionIdentity from, SelectedSessionIdentity to) =>
            await new DirectBus<GameRequestPeerCommand>(new GameRequestPeerCommandHandler(sessions, town, Parties,
                    new FixedPartyMemberStatus(), new TestCommandProjections(projection.Events), projection.Streams,
                    ProtocolAlgorithmFixture.Default<IOutboundOrdering>(), Runs,
                    NullLogger<GameRequestPeerCommandHandler>.Instance))
                .SendAsync(new GameRequestPeerCommand(Dispatch(from),
                    new RequestPeerRequest(GameMessageMetadata.Empty, to.WireUserId, 0, 64, [])));

        public async Task<bool> ResponsePeerAsync(SelectedSessionIdentity from, SelectedSessionIdentity inviter) =>
            await new DirectBus<GameResponsePeerCommand>(new GameResponsePeerCommandHandler(sessions, town, Parties,
                    new TestCommandProjections(projection.Events), projection.Streams,
                    ProtocolAlgorithmFixture.Default<IOutboundOrdering>(), Runs,
                    NullLogger<GameResponsePeerCommandHandler>.Instance))
                .SendAsync(new GameResponsePeerCommand(Dispatch(from),
                    new ResponsePeerRequest(GameMessageMetadata.Empty, inviter.WireUserId, 0, 0, [])));

        public void AssertPartyMembers(SelectedSessionIdentity leader, params SelectedSessionIdentity[] members)
        {
            var party = Assert.IsType<PartyMembershipSnapshot>(Parties.FindByKey(Parties.KeyOf(leader.CharacterId)));
            Assert.Equal(leader.CharacterId, party.LeaderCharacterId);
            Assert.Equal(
                members.Select(member => member.CharacterId).Prepend(leader.CharacterId),
                party.Members.Select(member => member.CharacterId));
        }

        /// <summary>The party and its run keep exactly the two entrants, and the outsider has no run.</summary>
        public void AssertRunPartyUnchanged(DungeonRunState run)
        {
            AssertPartyMembers(Inviter, Invitee);
            Assert.Equal(PartyKeyKind.Virtual, Parties.KeyOf(Outsider.CharacterId).Kind);
            Assert.Null(Runs.Find(Outsider.CharacterId));
            var current = Assert.IsType<DungeonRunState>(Runs.Find(run.RunId));
            Assert.Equal(run.EntryRevision, current.EntryRevision);
            Assert.Equal([Inviter.CharacterId, Invitee.CharacterId],
                current.Participants.Select(participant => participant.CharacterId));
            Assert.Equal(Inviter.CharacterId, Assert.Single(current.Participants, p => p.IsLeader).CharacterId);
        }

        private DungeonRunState Enter(params DungeonRunParticipant[] roster) =>
            Runs.PreparePartyEntry(roster, Layout, 0,
                DungeonCellSeed.CompleteEmpty(Layout.StartCell, Layout.StartRoomMapId),
                DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
    }
}
