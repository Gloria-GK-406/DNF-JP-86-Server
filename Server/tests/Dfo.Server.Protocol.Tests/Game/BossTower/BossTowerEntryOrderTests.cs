using Dfo.Server.Testing;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Dfo.Server.Protocol.Tests.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Outbox;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.BossTower;

/// <summary>XO-3 (spec appendix D): every member's tower entry batch is delivered before the requester's.</summary>
public sealed partial class BossTowerProjectionConcurrencyTests
{
    [Fact]
    public async Task A_slow_member_is_delivered_the_tower_entry_before_the_requester()
    {
        var requester = Session(1);
        var member = Session(2);
        await using var outbound = new XoDelivery();
        outbound.Connect(requester, member);
        outbound.Transport.Slow(member.ConnectionId, TimeSpan.FromMilliseconds(300));

        await outbound.RunAsync(async () =>
        {
            var towers = new TowerDomain();
            var sessions = new Sessions(requester, member);
            var views = new EntryViews(requester, member);
            var roster = EntryRoster(requester, member);
            var delivery = Dfo.Server.Protocol.Tests.Game.DungeonEntry.TowerProjectionTestFactory.Boss(
                towers, new EntryCommands(towers, requester, member), sessions, new EntryParties(roster), views,
                new SameState(), null!, null!, new Intents(), outbound.Game,
                TownAreaProjectionTestFactory.Create(sessions, views, outbound.Game), TimeProvider.System, outbound.Closer,
                NullLogger<BossTowerProjectionDriver>.Instance, ordering: outbound.Ordering, onlineUsers: ProtocolAlgorithmFixture.QueryFor(sessions, outbound.Context));

            Assert.True(await delivery.EnterAsync(
                requester.ConnectionId, requester, roster, DungeonEntryIntent.Interactive, () => { }, CancellationToken.None));
        });
        await outbound.SettledAsync();

        var memberEntry = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == member.ConnectionId);
        var requesterEntry = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == requester.ConnectionId);
        Assert.True(memberEntry.Order!.Value.Sequence < requesterEntry.Order!.Value.Sequence);
        Assert.Equal(OutboundDelivery.BestEffort, requesterEntry.Delivery);
        Assert.True(outbound.Delivered(requester.ConnectionId) > outbound.Delivered(member.ConnectionId),
            "the requester entered before the member");
    }

    /// <summary>
    /// DR-3, VC-3 (TASK-24 T3): a member whose tower entry fails in transport is closed by the sender alone;
    /// the requester still enters and the delivery registers no close.
    /// </summary>
    [Fact]
    public async Task A_member_whose_transport_fails_is_closed_alone_and_the_requester_still_enters_the_tower()
    {
        var requester = Session(1);
        var member = Session(2);
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(requester, member);
        outbound.Transport.Fail(member.ConnectionId);

        var entered = await outbound.RunAsync(() => EnterThroughOutboxAsync(outbound, requester, member));
        await outbound.SettledAsync();

        Assert.True(entered);
        var memberEntry = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == member.ConnectionId);
        var requesterEntry = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == requester.ConnectionId);
        Assert.Equal(Dfo.Server.Abstractions.Execution.OutboundOutcome.Failed, outbound.Observer.OutcomeOf(memberEntry));
        Assert.True(await outbound.ClosedAsync(member.ConnectionId));
        Assert.Equal(Dfo.Server.Abstractions.Execution.OutboundOutcome.Delivered, outbound.Observer.OutcomeOf(requesterEntry));
        Assert.False(outbound.Transport.IsClosed(requester.ConnectionId));
        Assert.DoesNotContain(outbound.Recorder.Entries, entry => entry.IsClose);
    }

    /// <summary>
    /// DR-3, the kept entry branch (TASK-24 R3): a member's entry batch cannot be registered (its encoding fails),
    /// so every isolated member and the requester are closed and the failure is thrown, as before the outbox.
    /// </summary>
    [Fact]
    public async Task A_member_entry_that_cannot_be_registered_isolates_every_member_and_the_requester()
    {
        var requester = Session(1);
        var member = Session(2);
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(requester, member);
        outbound.Encoder.Fail<Dfo.Server.Protocol.Game.DungeonSelection.EnterSelectDungeonAcknowledgement>(member.ConnectionId);

        await Assert.ThrowsAsync<InvalidDataException>(() => outbound.RunFromAsync(
            requester.ConnectionId, () => EnterThroughOutboxAsync(outbound, requester, member)));
        await outbound.SettledAsync();

        Assert.True(outbound.CloseRegistered(member.ConnectionId));
        Assert.True(outbound.CloseRegistered(requester.ConnectionId));
        Assert.Empty(outbound.Messages(member.ConnectionId));
        Assert.Empty(outbound.Messages(requester.ConnectionId));
        Assert.True(await outbound.ClosedAsync(member.ConnectionId));
        Assert.True(await outbound.ClosedAsync(requester.ConnectionId));
    }

    /// <summary>
    /// DR-3, the kept projection branch (TASK-24 R3): one remaining member's departure projection cannot be
    /// registered; only he is closed, the others keep theirs.
    /// </summary>
    [Fact]
    public async Task A_departure_projection_that_cannot_be_registered_for_one_member_closes_only_him()
    {
        var leaving = Session(1);
        var second = Session(2);
        var third = Session(3);
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(leaving, second, third);
        outbound.Encoder.Fail<Dfo.Server.Protocol.Game.DungeonSelection.UdpHostNotification>(second.ConnectionId);

        await outbound.RunAsync(async () =>
        {
            var sessions = new Sessions(leaving, second, third);
            var run = PartyRun(Guid.NewGuid(), leaving, second, third);
            var towers = new TowerDomain(run with { Room = run.Room with { Phase = Dfo.Server.Abstractions.World.IllusionTower.TowerRoomPhase.Fighting } });
            var views = new NoViews();
            var delivery = Dfo.Server.Protocol.Tests.Game.DungeonEntry.TowerProjectionTestFactory.BossLegacy(
                towers, new Commands(), sessions, null!, views, null!, null!, null!, new Intents(), outbound.Game,
                global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.TownProjection.TownAreaProjectionDelivery>(null!, sessions, views, null!, null!, outbound.Game, null!),
                TimeProvider.System, outbound.Closer, NullLogger<BossTowerProjectionDriver>.Instance, onlineUsers: ProtocolAlgorithmFixture.QueryFor(sessions, outbound.Context));
            await delivery.ProjectCommittedDepartureAsync(towers.Leave(leaving), returning: null, response: null, CancellationToken.None);
        });
        await outbound.SettledAsync();

        Assert.True(outbound.CloseRegistered(second.ConnectionId));
        Assert.False(outbound.CloseRegistered(third.ConnectionId));
        Assert.False(outbound.CloseRegistered(leaving.ConnectionId));
        Assert.Contains(outbound.Messages(third.ConnectionId), message => message is Dfo.Server.Protocol.Game.DungeonSelection.UdpHostNotification);
    }

    private static async Task<bool> EnterThroughOutboxAsync(
        Dfo.Server.Protocol.Tests.Outbox.XoDelivery outbound, SelectedSessionIdentity requester, SelectedSessionIdentity member)
    {
        var towers = new TowerDomain();
        var sessions = new Sessions(requester, member);
        var views = new EntryViews(requester, member);
        var roster = EntryRoster(requester, member);
        var delivery = Dfo.Server.Protocol.Tests.Game.DungeonEntry.TowerProjectionTestFactory.Boss(
            towers, new EntryCommands(towers, requester, member), sessions, new EntryParties(roster), views,
            new SameState(), null!, null!, new Intents(), outbound.Game,
            TownAreaProjectionTestFactory.Create(sessions, views, outbound.Game), TimeProvider.System, outbound.Closer,
            NullLogger<BossTowerProjectionDriver>.Instance, ordering: outbound.Ordering, onlineUsers: ProtocolAlgorithmFixture.QueryFor(sessions, outbound.Context));
        return await delivery.EnterAsync(
            requester.ConnectionId, requester, roster, DungeonEntryIntent.Interactive, () => { }, CancellationToken.None);
    }

    private static PartySnapshot EntryRoster(params SelectedSessionIdentity[] members) => new(
        77, 1, 1, members[0].WireUserId,
        new PartySettings(new byte[PartySettings.WireLength]), PartyTitle.Empty,
        [.. members.Select((session, slot) => new PartyMemberSnapshot(PartyCharacter(session), checked((byte)slot), null, 100))]);

    private static SelectCharacterResult EntryOf(SelectedSessionIdentity session)
    {
        var entry = SelectCharacterPayloadTestData.CreateComplete();
        return entry with
        {
            Character = entry.Character with
            {
                Identity = entry.Character.Identity with
                {
                    CharacterId = session.CharacterId,
                    Name = [(byte)(session.WireUserId & byte.MaxValue)],
                },
            },
        };
    }

    private sealed class EntryViews(params SelectedSessionIdentity[] sessions) : ISelectedCharacterCurrentViewDomainService
    {
        public ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken cancellationToken) =>
            ValueTask.FromResult<SelectCharacterResult?>(sessions.Contains(session) ? EntryOf(session) : null);
    }

    private sealed class SameState : ICharacterStateProjectionDomainService
    {
        public ValueTask<SelectCharacterResult> RefreshAsync(SelectCharacterResult result, CancellationToken cancellationToken) =>
            ValueTask.FromResult(result);
    }

    private sealed class EntryParties(PartySnapshot snapshot) : IPartyService
    {
        public PartyMembershipSnapshot? FindByKey(PartyKey key) => null;
        public PartySnapshot FindParty(PartyCharacterSnapshot character) => snapshot;
        public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope) { }
        public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => [snapshot];
        public PartyOperationResult CreateOrUpdateSettings(PartyCharacterSnapshot character, PartySettings settings, PartyTitle title) => throw new NotSupportedException();
        public PartyOperationResult RequestInvite(PartyCharacterSnapshot inviter, PartyCharacterSnapshot target, PartyInviteDisplayState display) => throw new NotSupportedException();
        public PartyOperationResult AcceptInvite(PartyCharacterSnapshot invitee, PartyCharacterSnapshot inviter) => throw new NotSupportedException();
        public PartyOperationResult TransferLeadership(CharacterId currentLeaderCharacterId, CharacterId nextLeaderCharacterId) => throw new NotSupportedException();
        public PartyOperationResult RemoveMember(CharacterId currentLeaderCharacterId, CharacterId targetCharacterId) => throw new NotSupportedException();
        public PartyOperationResult LeaveParty(CharacterId characterId) => throw new NotSupportedException();
        public PartyOperationResult CleanupCharacter(CharacterId characterId) => throw new NotSupportedException();
    }

    private sealed class EntryCommands(TowerDomain towers, params SelectedSessionIdentity[] members) : ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            if (command is not Dfo.Server.Protocol.Tests.Game.DungeonEntry.BossEntryRouteProbe)
                throw new NotSupportedException(command.GetType().Name);
            var run = PartyRun(Guid.NewGuid(), members);
            towers.Put(run);
            return ValueTask.FromResult((TResult)(object)new BossTowerEntryResult(null, run, []));
        }
    }
}
