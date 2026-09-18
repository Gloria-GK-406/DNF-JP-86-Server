using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Dfo.Server.Protocol.Tests.Game.TownProjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using EnteredDungeon = Dfo.Server.Abstractions.World.DungeonEntry;

namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;

public sealed class PartySelectDungeonHandlerTests
{
    [Fact]
    public async Task Only_the_leader_can_return_the_frozen_dungeon_cohort_to_town()
    {
        var participants = new[]
        {
            new DungeonRunParticipant(Leader.AccountId, Leader.CharacterId, 0, true),
            new DungeonRunParticipant(Member.AccountId, Member.CharacterId, 1, false),
        };
        var layout = PartyVisits.LayoutForTest();
        var runId = DungeonRunId.New();
        var baseRun = new DungeonRunState(
            layout, layout.StartCell, layout.StartRoomMapId,
            new HashSet<MazeCell> { layout.StartCell }, DateTimeOffset.UnixEpoch,
            RunId: runId, Participant: participants[0], Participants: participants,
            CurrentCellState: DungeonCellSeed.CompleteEmpty(
                layout.StartCell, layout.StartRoomMapId).Snapshot());
        var runs = new RollbackRuns();
        runs.SetCurrent(Leader.CharacterId, baseRun);
        runs.SetCurrent(Member.CharacterId, baseRun with { Participant = participants[1] });
        var sessions = new Sessions(Leader, Member);
        var runtime = new PartyRuntime((Leader, EntryFor(Leader)), (Member, EntryFor(Member)));
        var sender = new Sender();
        var deliveries = new DungeonRunDeliveryRegistry();
        deliveries.Register(runId, [Leader, Member]);
        var handler = new EplpCommandHandler(
            sessions, runtime, new PartyPositions(), runs, deliveries, sender,
            TownAreaProjectionTestFactory.Create(sessions, runtime, sender),
            NullLogger<EplpCommandHandler>.Instance,
            new SelectedSessionTransitionGate());
        var request = new EplpCommandRequest(GameMessageMetadata.Empty, 1, 2);

        await handler.HandleAsync(Dispatch(Member), request, CancellationToken.None);
        Assert.Empty(runs.Ended);

        await handler.HandleAsync(Dispatch(Leader),
            new EplpCommandRequest(GameMessageMetadata.Empty, 2, 2),
            CancellationToken.None);
        Assert.Empty(runs.Ended);
        Assert.Contains(sender.Messages(Member.ConnectionId),
            message => message is EplpCommandResponse { State: 2, Option: 2 });

        await handler.HandleAsync(Dispatch(Leader), request, CancellationToken.None);
        Assert.Equal([Leader.CharacterId, Member.CharacterId], runs.Ended);
        foreach (var session in new[] { Leader, Member })
        {
            Assert.Contains(sender.Messages(session.ConnectionId),
                message => message is EplpCommandResponse { State: 1, Option: 2 });
            Assert.Contains(sender.Messages(session.ConnectionId),
                message => message is UserAreaNotification);
        }
        Assert.Empty(deliveries.Find(runId));
    }

    [Fact]
    public async Task The_leader_opens_one_selection_context_for_every_party_member()
    {
        var sessions = new Sessions(Leader, Member);
        var runtime = new Runtime(SelectCharacterPayloadTestData.CreateComplete(), [Leader, Member]);
        var intents = new Intents();
        var sender = new Sender();
        var handler = new EnterSelectDungeonHandler(
            new UnchangedState(),
            sessions,
            runtime,
            new PartyVisits(),
            intents,
            sender,
            TownAreaProjectionTestFactory.Create(sessions, runtime, sender),
            NullLogger<EnterSelectDungeonHandler>.Instance,
            new Parties(Snapshot()));

        await handler.HandleAsync(
            Dispatch(Leader),
            new EnterSelectDungeonRequest(GameMessageMetadata.Empty, DungeonId),
            CancellationToken.None);

        var expectedUsers = new[] { Leader.WireUserId, Member.WireUserId };
        Assert.Equal(
            expectedUsers,
            Assert.Single(sender.Messages(Leader.ConnectionId)
                .OfType<EnterSelectDungeonNotification>()).WireUserIds);
        Assert.Equal(
            expectedUsers,
            Assert.Single(sender.Messages(Member.ConnectionId)
                .OfType<EnterSelectDungeonNotification>()).WireUserIds);
        Assert.NotNull(intents.Consume(Leader));
        Assert.NotNull(intents.Consume(Member));
    }

    [Fact]
    public async Task A_follower_cannot_open_a_solo_selection_while_in_a_party()
    {
        var sessions = new Sessions(Leader, Member);
        var runtime = new Runtime(SelectCharacterPayloadTestData.CreateComplete(), [Leader, Member]);
        var intents = new Intents();
        var sender = new Sender();
        var handler = new EnterSelectDungeonHandler(
            new UnchangedState(), sessions, runtime, new PartyVisits(), intents, sender,
            TownAreaProjectionTestFactory.Create(sessions, runtime, sender),
            NullLogger<EnterSelectDungeonHandler>.Instance, new Parties(Snapshot()));

        await handler.HandleAsync(
            Dispatch(Member), new EnterSelectDungeonRequest(GameMessageMetadata.Empty, DungeonId),
            CancellationToken.None);

        Assert.Empty(sender.Messages(Member.ConnectionId));
        Assert.Null(intents.Consume(Member));
        Assert.Null(intents.Consume(Leader));
    }

    [Fact]
    public async Task The_leader_cancels_selection_for_every_member_and_restores_the_shared_town()
    {
        var sessions = new Sessions(Leader, Member);
        var runtime = new PartyRuntime(
            (Leader, EntryFor(Leader)),
            (Member, EntryFor(Member)));
        var intents = new Intents();
        intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));
        intents.Record(Member, DungeonEntryIntent.Directed(DungeonId));
        var sender = new Sender();
        using var services = new ServiceCollection()
            .AddSingleton<ISelectedSessionDomainService>(sessions)
            .AddSingleton<ISelectedSessionRuntimeDirectory>(runtime)
            .AddSingleton<ICharacterPositionDomainService>(new PartyPositions())
            .AddSingleton<IDungeonRunDomainService>(new RollbackRuns())
            .AddSingleton(new DungeonRunDeliveryRegistry())
            .AddSingleton<IDungeonEntryIntentDomainService>(intents)
            .AddSingleton<IPartyService>(new Parties(Snapshot()))
            .AddSingleton<IGameMessageSender>(sender)
            .AddSingleton(TownAreaProjectionTestFactory.Create(sessions, runtime, sender))
            .AddSingleton<SelectedSessionTransitionGate>()
            .AddSingleton<Microsoft.Extensions.Logging.ILogger<BackToVillageHandler>>(
                NullLogger<BackToVillageHandler>.Instance)
            .BuildServiceProvider();
        var handler = ActivatorUtilities.CreateInstance<BackToVillageHandler>(services);

        await handler.HandleAsync(
            Dispatch(Leader),
            new BackToVillageRequest(GameMessageMetadata.Empty),
            CancellationToken.None);

        Assert.IsType<BackToVillageResponse>(sender.Messages(Leader.ConnectionId).First());
        Assert.DoesNotContain(
            sender.Messages(Member.ConnectionId),
            frame => frame is BackToVillageResponse);
        foreach (var participant in new[] { Leader, Member })
        {
            var messages = sender.Messages(participant.ConnectionId);
            Assert.Contains(messages, frame => frame is UserStateNotification state
                && state.WireUserId == participant.WireUserId
                && state.UserState == 0);
            var roster = Assert.Single(messages.OfType<AreaUsersNotification>());
            Assert.Equal(
                new[] { Leader.WireUserId, Member.WireUserId }.Order(),
                roster.Users.Select(value => value.WireUserId).Order());
            Assert.Contains(
                messages.OfType<CurrentUserStateMessage>(),
                frame => frame.Character.Identity.CharacterId != participant.CharacterId);
        }
        Assert.Null(intents.Consume(Leader));
        Assert.Null(intents.Consume(Member));

        var memberFramesBeforeLateRequest = sender.Messages(Member.ConnectionId).Count;
        await handler.HandleAsync(
            Dispatch(Member),
            new BackToVillageRequest(GameMessageMetadata.Empty),
            CancellationToken.None);

        var lateFrames = sender.Messages(Member.ConnectionId)
            .Skip(memberFramesBeforeLateRequest)
            .ToArray();
        Assert.IsType<BackToVillageResponse>(Assert.Single(lateFrames));
    }

    [Fact]
    public async Task A_party_member_cannot_select_the_story_dungeon_for_the_party()
    {
        var world = World();
        world.Intents.Record(Member, DungeonEntryIntent.Directed(DungeonId));

        await world.Handler.HandleAsync(Dispatch(Member), Request(), CancellationToken.None);

        var refusal = Assert.IsType<SelectDungeonRefusal>(
            Assert.Single(world.Sender.Messages(Member.ConnectionId)));
        Assert.Equal(SelectDungeonRefusal.NotPartyLeader, refusal.ErrorCode);
        Assert.Equal(0, world.Visits.PartyEntries);
        Assert.Empty(world.Registry.Find(world.Visits.RunId));
    }

    [Fact]
    public async Task An_unattachable_member_refuses_the_whole_party_before_a_run_is_created()
    {
        var world = World(memberRuntimeAvailable: false);
        world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));

        await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        var refusal = Assert.IsType<SelectDungeonRefusal>(
            Assert.Single(world.Sender.Messages(Leader.ConnectionId)));
        Assert.Equal(SelectDungeonRefusal.MemberBlocked, refusal.ErrorCode);
        Assert.Equal(1, refusal.Context);
        Assert.Equal(0, world.Visits.PartyEntries);
        Assert.Empty(world.Fatigue.Spent);
    }

    [Fact]
    public async Task The_leader_enters_every_frozen_member_and_supplies_a_missing_selection_prelude()
    {
        var world = World();
        world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));

        await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        Assert.Equal(1, world.Visits.PartyEntries);
        Assert.Collection(
            world.Visits.Participants,
            participant =>
            {
                Assert.Equal(Leader.CharacterId, participant.CharacterId);
                Assert.True(participant.IsLeader);
                Assert.Equal(0, participant.Slot);
            },
            participant =>
            {
                Assert.Equal(Member.CharacterId, participant.CharacterId);
                Assert.False(participant.IsLeader);
                Assert.Equal(1, participant.Slot);
            });
        Assert.Equal(world.Visits.RunId, world.Visits.Visits[Leader.CharacterId].Run.RunId);
        Assert.Equal(world.Visits.RunId, world.Visits.Visits[Member.CharacterId].Run.RunId);
        Assert.Equal(
            world.Visits.Visits[Leader.CharacterId].Run.CurrentCell,
            world.Visits.Visits[Member.CharacterId].Run.CurrentCell);
        Assert.Equal(
            [Leader, Member],
            world.Registry.Find(world.Visits.RunId));

        Assert.Collection(
            world.Sender.Messages(Leader.ConnectionId),
            frame => Assert.IsType<ExtendedUserStateMessage>(frame),
            frame => Assert.IsType<ExtendedUserStateMessage>(frame),
            frame => Assert.IsType<EnterSelectDungeonAcknowledgement>(frame),
            frame => Assert.IsType<UserStateNotification>(frame),
            frame => Assert.IsType<UdpHostNotification>(frame),
            frame => Assert.IsType<SupportTagNotification>(frame),
            frame => Assert.IsType<DungeonInfoNotification>(frame),
            frame => Assert.IsType<StartMapNotification>(frame));
        Assert.Collection(
            world.Sender.Messages(Member.ConnectionId),
            frame => Assert.IsType<ExtendedUserStateMessage>(frame),
            frame => Assert.IsType<ExtendedUserStateMessage>(frame),
            frame => Assert.IsType<EnterSelectDungeonAcknowledgement>(frame),
            frame => Assert.IsType<UserStateNotification>(frame),
            frame => Assert.IsType<UdpHostNotification>(frame),
            frame => Assert.IsType<EnterSelectDungeonNotification>(frame),
            frame => Assert.IsType<SupportTagNotification>(frame),
            frame => Assert.IsType<DungeonInfoNotification>(frame),
            frame => Assert.IsType<StartMapNotification>(frame));
        Assert.Equal(
            new[] { Leader.WireUserId, Member.WireUserId },
            Assert.Single(world.Sender.Messages(Member.ConnectionId)
                .OfType<EnterSelectDungeonNotification>()).WireUserIds);
        Assert.Equal(
            new ushort?[] { null, Member.WireUserId },
            world.Sender.Messages(Leader.ConnectionId)
                .OfType<ExtendedUserStateMessage>()
                .Select(value => value.RoutingWireUserId));
        Assert.Equal(
            new ushort?[] { null, Leader.WireUserId },
            world.Sender.Messages(Member.ConnectionId)
                .OfType<ExtendedUserStateMessage>()
                .Select(value => value.RoutingWireUserId));
        var leaderUserInfos = world.Sender.Messages(Leader.ConnectionId)
            .OfType<ExtendedUserStateMessage>().Select(value => value.EncodeBody()).ToArray();
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(leaderUserInfos[0].AsSpan(3)));
        Assert.Equal(
            checked((ushort)Leader.CharacterId.Value),
            BinaryPrimitives.ReadUInt16LittleEndian(leaderUserInfos[0].AsSpan(18)));
        Assert.Equal(
            Member.WireUserId,
            BinaryPrimitives.ReadUInt16LittleEndian(leaderUserInfos[1].AsSpan(3)));
        Assert.Equal(
            checked((ushort)Member.CharacterId.Value),
            BinaryPrimitives.ReadUInt16LittleEndian(leaderUserInfos[1].AsSpan(18)));
        foreach (var recipient in new[] { Leader, Member })
        {
            Assert.Equal(
                new[] { Leader.WireUserId, Member.WireUserId },
                Assert.Single(world.Sender.Messages(recipient.ConnectionId)
                    .OfType<UserStateNotification>()).WireUserIds);
            Assert.Equal(
                0,
                Assert.Single(world.Sender.Messages(recipient.ConnectionId)
                    .OfType<UdpHostNotification>()).HostSlot);
        }
        var leaderStart = Assert.Single(world.Sender.Messages(Leader.ConnectionId)
            .OfType<StartMapNotification>());
        var memberStart = Assert.Single(world.Sender.Messages(Member.ConnectionId)
            .OfType<StartMapNotification>());
        Assert.Equal<byte?>(0, leaderStart.Run.Participant?.Slot);
        Assert.Equal<byte?>(1, memberStart.Run.Participant?.Slot);
        Assert.Equal(leaderStart.Run.CurrentRoomMapId, memberStart.Run.CurrentRoomMapId);
        Assert.Equal(leaderStart.Actors, memberStart.Actors);
        Assert.Equal(leaderStart.Drops, memberStart.Drops);
        Assert.Equal(
            [Leader.CharacterId, Member.CharacterId],
            world.Fatigue.Spent.Select(value => value.CharacterId));
    }

    [Fact]
    public async Task An_interactive_party_selection_enters_one_shared_party_run()
    {
        var world = World();
        world.Intents.Record(Leader, DungeonEntryIntent.Interactive);

        await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        Assert.Equal(1, world.Visits.PartyEntries);
        Assert.Collection(
            world.Visits.Participants,
            participant =>
            {
                Assert.Equal(Leader.CharacterId, participant.CharacterId);
                Assert.Equal(0, participant.Slot);
                Assert.True(participant.IsLeader);
            },
            participant =>
            {
                Assert.Equal(Member.CharacterId, participant.CharacterId);
                Assert.Equal(1, participant.Slot);
                Assert.False(participant.IsLeader);
            });
        Assert.Equal(world.Visits.RunId, world.Visits.Visits[Leader.CharacterId].Run.RunId);
        Assert.Equal(world.Visits.RunId, world.Visits.Visits[Member.CharacterId].Run.RunId);
        Assert.Equal([Leader, Member], world.Registry.Find(world.Visits.RunId));
        Assert.Contains(
            world.Sender.Messages(Member.ConnectionId),
            frame => frame is StartMapNotification start
                && start.Run.Participant?.Slot == 1
                && start.Run.Participants.Count == 2);
    }

    [Fact]
    public async Task Entering_an_interactive_party_run_replaces_the_whole_old_party_delivery()
    {
        var world = World();
        var oldRunId = DungeonRunId.New();
        var oldRun = world.Visits.Visits.Count == 0
            ? new DungeonRunState(
                PartyVisits.LayoutForTest(),
                new MazeCell(0, 0),
                61000,
                new HashSet<MazeCell> { new(0, 0) },
                DateTimeOffset.UnixEpoch,
                RunId: oldRunId,
                CurrentCellState: DungeonCellSeed.CompleteEmpty(
                    new MazeCell(0, 0), 61000).Snapshot())
            : throw new InvalidOperationException();
        world.Runs.SetCurrent(Leader.CharacterId, oldRun);
        world.Runs.SetCurrent(Member.CharacterId, oldRun);
        world.Registry.Register(oldRunId, [Leader, Member]);
        world.Intents.Record(Leader, DungeonEntryIntent.Interactive);

        await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        Assert.Empty(world.Registry.Find(oldRunId));
        Assert.Equal([Leader, Member], world.Registry.Find(world.Visits.RunId));
    }

    [Fact]
    public async Task A_generation_change_during_delivery_preparation_rolls_back_without_run_frames()
    {
        var world = World(replaceMemberDuringPrelude: true, withTownProjection: true);
        world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));

        await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        Assert.Empty(world.Registry.Find(world.Visits.RunId));
        Assert.Equal(
            [Leader.CharacterId, Member.CharacterId],
            world.Runs.Ended.OrderBy(value => value.Value));
        Assert.DoesNotContain(world.Sender.Messages(Leader.ConnectionId), frame => frame is StartMapNotification);
        Assert.DoesNotContain(world.Sender.Messages(Member.ConnectionId), frame => frame is StartMapNotification);
        Assert.True(Leader.Owns(world.Areas.Find(Leader.CharacterId)!));
    }

    [Fact]
    public async Task A_newer_entry_revision_is_reread_and_projected_without_destroying_the_run()
    {
        var world = World(advanceEntryRevisionDuringPrelude: true, withTownProjection: true);
        world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));

        await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        Assert.Equal([Leader, Member], world.Registry.Find(world.Visits.RunId));
        Assert.Empty(world.Runs.Ended);
        Assert.Contains(
            world.Sender.Messages(Leader.ConnectionId),
            frame => frame is StartMapNotification);
        Assert.Contains(
            world.Sender.Messages(Member.ConnectionId),
            frame => frame is StartMapNotification);
        Assert.Equal(
            3,
            Assert.Single(world.Sender.Messages(Leader.ConnectionId)
                .OfType<StartMapNotification>()).Run.EntryRevision);
        Assert.Null(world.Areas.Find(Leader.CharacterId));
        Assert.Null(world.Areas.Find(Member.CharacterId));
    }

    [Fact]
    public async Task Cancelling_while_acquiring_commit_gates_rolls_back_before_leaving_town()
    {
        var world = World(withTownProjection: true);
        world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));
        var blocker = await world.TransitionGate.AcquireAsync(
            Leader.AccountId,
            UserAccessMode.Exclusive,
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var pending = world.Handler.HandleAsync(
            Dispatch(Leader), Request(), cancellation.Token).AsTask();
        await Task.Yield();
        Assert.False(pending.IsCompleted);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await blocker.DisposeAsync();

        Assert.Equal(
            [Leader.CharacterId, Member.CharacterId],
            world.Runs.Ended.OrderBy(value => value.Value));
        Assert.True(Leader.Owns(world.Areas.Find(Leader.CharacterId)!));
        Assert.True(Member.Owns(world.Areas.Find(Member.CharacterId)!));
    }

    [Fact]
    public async Task A_projection_failure_cannot_split_party_members_between_town_and_run()
    {
        var world = World(
            withTownProjection: true,
            partyTownProjection: new ThrowingPartyTownProjection());
        world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Handler.HandleAsync(
            Dispatch(Leader), Request(), CancellationToken.None).AsTask());

        Assert.Null(world.Areas.Find(Leader.CharacterId));
        Assert.Null(world.Areas.Find(Member.CharacterId));
        Assert.Empty(world.Runs.Ended);
    }

    [Fact]
    public async Task A_secondary_recipient_delivery_failure_aborts_entry_before_anyone_leaves_town()
    {
        var world = World(
            withTownProjection: true,
            failRequiredConnection: Member.ConnectionId);
        world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Handler.HandleAsync(
            Dispatch(Leader), Request(), CancellationToken.None).AsTask());

        Assert.Empty(world.Registry.Find(world.Visits.RunId));
        Assert.Equal(
            [Leader.CharacterId, Member.CharacterId],
            world.Runs.Ended.OrderBy(value => value.Value));
        Assert.DoesNotContain(
            world.Sender.Messages(Leader.ConnectionId),
            frame => frame is StartMapNotification);
        Assert.Equal([Member.ConnectionId], world.Closer.Closed);
        Assert.True(Leader.Owns(world.Areas.Find(Leader.CharacterId)!));
        Assert.True(Member.Owns(world.Areas.Find(Member.CharacterId)!));
    }

    [Fact]
    public async Task A_leader_delivery_failure_isolates_the_member_that_already_received_start_map()
    {
        var world = World(
            withTownProjection: true,
            failRequiredConnection: Leader.ConnectionId);
        world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Handler.HandleAsync(
            Dispatch(Leader), Request(), CancellationToken.None).AsTask());

        Assert.Contains(
            world.Sender.Messages(Member.ConnectionId),
            frame => frame is StartMapNotification);
        Assert.Equal(
            [Member.ConnectionId, Leader.ConnectionId],
            world.Closer.Closed);
        Assert.Empty(world.Registry.Find(world.Visits.RunId));
        Assert.Equal(
            [Leader.CharacterId, Member.CharacterId],
            world.Runs.Ended.OrderBy(value => value.Value));
        Assert.True(Leader.Owns(world.Areas.Find(Leader.CharacterId)!));
        Assert.True(Member.Owns(world.Areas.Find(Member.CharacterId)!));
    }

    private static TestWorld World(
        bool memberRuntimeAvailable = true,
        bool replaceMemberDuringPrelude = false,
        bool advanceEntryRevisionDuringPrelude = false,
        bool withTownProjection = false,
        IPartyTownProjectionDelivery? partyTownProjection = null,
        NetworkConnectionId? failRequiredConnection = null)
    {
        var sessions = new Sessions(Leader, Member);
        var runtime = memberRuntimeAvailable
            ? new PartyRuntime((Leader, EntryFor(Leader)), (Member, EntryFor(Member)))
            : new PartyRuntime((Leader, EntryFor(Leader)));
        var runs = new RollbackRuns();
        var visits = new PartyVisits(runs);
        var intents = new Intents();
        var registry = new DungeonRunDeliveryRegistry();
        var fatigue = new Fatigue();
        var sender = new Sender(failRequiredConnection);
        var closer = new RecordingCloser();
        var areas = new SessionPresenceDomainService();
        foreach (var member in new[] { Leader, Member })
        {
            areas.Admit(new SessionPresence(
                member.CharacterId,
                member.ListenerPort,
                1,
                1,
                true,
                member,
                true,
                1));
        }
        var townProjection = new TownAreaProjectionDelivery(
            areas,
            sessions,
            runtime,
            new NoopNotificationBus(),
            partyTownProjection ?? new NoopPartyTownProjection(),
            sender,
            SharedTownMovementDomainService.Instance);
        var transitionGate = new SelectedSessionTransitionGate();
        var state = new UnchangedState(() =>
        {
            if (!replaceMemberDuringPrelude)
            {
                if (advanceEntryRevisionDuringPrelude
                    && runs.Find(Leader.CharacterId) is { } current)
                {
                    foreach (var participant in new[] { Leader, Member })
                        runs.SetCurrent(participant.CharacterId, current with
                        {
                            Participant = current.Participants.Single(value =>
                                value.CharacterId == participant.CharacterId),
                            EntryRevision = current.EntryRevision + 1,
                        });
                }
                return;
            }
            sessions.Replace(Member.CharacterId, Member with
            {
                ConnectionId = new NetworkConnectionId("member-replacement"),
                ConnectionGeneration = Member.ConnectionGeneration + 1,
            });
        });
        var handler = new SelectDungeonHandler(
            CurrentCharacterStub.Domain(),
            state,
            sessions,
            runtime,
            new Parties(Snapshot()),
            runs,
            registry,
            intents,
            visits,
            new RecordingQuestLogs(),
            fatigue,
            sender,
            closer,
            townProjection,
            NullLogger<SelectDungeonHandler>.Instance,
            transitionGate);
        return new TestWorld(
            handler, visits, intents, registry, runs, fatigue, sender, closer, areas, transitionGate);
    }

    private static SelectCharacterResult Entry() => SelectCharacterPayloadTestData.Create();

    private static SelectCharacterResult EntryFor(SelectedSessionIdentity session)
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

    private static SelectDungeonRequest Request() =>
        new(GameMessageMetadata.Empty, DungeonId, Difficulty, 0, 0);

    private static NetworkDispatchContext Dispatch(SelectedSessionIdentity session) =>
        new(new NetworkConnectionContext(
            session.ConnectionId, NetworkEndpointKind.Game, session.ListenerPort));

    private static PartySnapshot Snapshot() => new(
        55,
        1,
        1,
        Leader.WireUserId,
        new PartySettings([0, 0, 4, 0, 0, 0, 0, 5, 0, (byte)PartyCategory.Story, 0, 0]),
        PartyTitle.Empty,
        ImmutableArray.Create(
            new PartyMemberSnapshot(Character(Leader), 0, null, 100),
            new PartyMemberSnapshot(Character(Member), 1, null, 100)));

    private static PartyCharacterSnapshot Character(SelectedSessionIdentity session) =>
        new(session.AccountId, session.CharacterId, session.WireUserId, new PartyDirectoryScope(1, 1, 1));

    private static readonly SelectedSessionIdentity Leader = SelectedSessionIdentity.Create(
        new AccountId(1), new CharacterId(1001), new NetworkConnectionId("leader"), 11, 10010);

    private static readonly SelectedSessionIdentity Member = SelectedSessionIdentity.Create(
        new AccountId(2), new CharacterId(1002), new NetworkConnectionId("member"), 21, 10011);

    private const ushort DungeonId = 10000;
    private const byte Difficulty = 3;

    private sealed record TestWorld(
        SelectDungeonHandler Handler,
        PartyVisits Visits,
        Intents Intents,
        DungeonRunDeliveryRegistry Registry,
        RollbackRuns Runs,
        Fatigue Fatigue,
        Sender Sender,
        RecordingCloser Closer,
        ITownAreaDomainService Areas,
        SelectedSessionTransitionGate TransitionGate);

    private sealed class Parties(PartySnapshot snapshot) : IPartyService
    {
        public PartyMembershipSnapshot? FindByKey(PartyKey key) => null;
        public PartySnapshot FindParty(PartyCharacterSnapshot character) => snapshot;
        public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope) { }
        public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => [snapshot];
        public PartyOperationResult CreateOrUpdateSettings(PartyCharacterSnapshot character, PartySettings settings, PartyTitle title) => throw new NotSupportedException();
        public PartyOperationResult RequestInvite(PartyCharacterSnapshot inviter, PartyCharacterSnapshot target, PartyInviteDisplayState display) => throw new NotSupportedException();
        public PartyOperationResult AcceptInvite(PartyCharacterSnapshot invitee, PartyCharacterSnapshot inviter) => throw new NotSupportedException();
        public PartyOperationResult TransferLeadership(CharacterId currentLeaderCharacterId, CharacterId nextLeaderCharacterId) => throw new NotSupportedException();
        public PartyOperationResult LeaveParty(CharacterId characterId) => throw new NotSupportedException();
        public PartyOperationResult CleanupCharacter(CharacterId characterId) => throw new NotSupportedException();
    }

    private sealed class Sessions : ISelectedSessionDomainService
    {
        private readonly List<SelectedSessionIdentity> connections;
        private readonly Dictionary<CharacterId, SelectedSessionIdentity> currentByCharacter;

        public Sessions(params SelectedSessionIdentity[] sessions)
        {
            connections = [.. sessions];
            currentByCharacter = sessions.ToDictionary(value => value.CharacterId);
        }

        public void Replace(CharacterId characterId, SelectedSessionIdentity replacement)
        {
            currentByCharacter[characterId] = replacement;
            connections.Add(replacement);
        }

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            connections.SingleOrDefault(value => value.ConnectionId == connectionId);
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            currentByCharacter.GetValueOrDefault(characterId);
        public SelectedSessionIdentity? Find(AccountId accountId) =>
            currentByCharacter.Values.SingleOrDefault(value => value.AccountId == accountId);
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
            currentByCharacter.Values.SingleOrDefault(value => value.WireUserId == wireUserId);
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }

    private sealed class Runtime(
        SelectCharacterResult result,
        IReadOnlyList<SelectedSessionIdentity> available) : ISelectedSessionRuntimeDirectory
    {
        public bool TryGet(SelectedSessionIdentity session, out SelectCharacterResult? found, out bool persist)
        {
            found = available.Contains(session) ? result : null;
            persist = false;
            return found is not null;
        }
        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) => result;
        public void Publish(SelectedSessionIdentity session, SelectCharacterResult value, bool persist) => throw new NotSupportedException();
        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult value) => throw new NotSupportedException();
        public bool RemoveExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }

    private sealed class PartyRuntime(
        params (SelectedSessionIdentity Session, SelectCharacterResult Result)[] entries)
        : ISelectedSessionRuntimeDirectory
    {
        private readonly Dictionary<SelectedSessionIdentity, SelectCharacterResult> results =
            entries.ToDictionary(value => value.Session, value => value.Result);

        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool persist)
        {
            persist = false;
            return results.TryGetValue(session, out result);
        }

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) => results[session];
        public void Publish(SelectedSessionIdentity session, SelectCharacterResult value, bool persist) =>
            throw new NotSupportedException();
        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult value) =>
            throw new NotSupportedException();
        public bool RemoveExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }

    private sealed class PartyPositions : ICharacterPositionDomainService
    {
        public CharacterPositionState? Find(CharacterId characterId) =>
            characterId == Leader.CharacterId
                ? new CharacterPositionState(1, 1, 400, 300, 1)
                : characterId == Member.CharacterId
                    ? new CharacterPositionState(1, 1, 430, 300, 3)
                    : null;

        public void Place(SelectedSessionIdentity session, CharacterPositionState position) =>
            throw new NotSupportedException();
        public CharacterPositionState CrossInto(
            SelectedSessionIdentity session, byte townId, byte areaId, short x, short y) =>
            throw new NotSupportedException();
        public CharacterPositionState StepTo(
            SelectedSessionIdentity session, short x, short y, byte direction) =>
            throw new NotSupportedException();
        public bool ForgetExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }

    private sealed class Intents : IDungeonEntryIntentDomainService
    {
        private readonly Dictionary<SelectedSessionIdentity, DungeonEntryIntent> values = [];
        public void Record(SelectedSessionIdentity session, DungeonEntryIntent intent) => values[session] = intent;
        public DungeonEntryIntent? Consume(SelectedSessionIdentity session) =>
            values.Remove(session, out var value) ? value : null;
        public bool ClearExact(SelectedSessionIdentity session) => values.Remove(session);
    }

    private sealed class PartyVisits(RollbackRuns? runs = null) : IDungeonVisitDomainService
    {
        public int PartyEntries { get; private set; }
        public DungeonRunId RunId { get; } = DungeonRunId.New();
        public IReadOnlyList<DungeonRunParticipant> Participants { get; private set; } = [];
        public IReadOnlyDictionary<CharacterId, DungeonRoomVisit> Visits { get; private set; } =
            new Dictionary<CharacterId, DungeonRoomVisit>();

        public bool DefersSelectionState(byte characterLevel) => false;
        public ValueTask<EnteredDungeon?> EnterAsync(CharacterId characterId, int dungeonId, byte difficulty, DungeonEntryFacts facts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<DungeonPartyEntry?> EnterPartyAsync(IReadOnlyList<DungeonRunParticipant> participants, int dungeonId, byte difficulty, DungeonEntryFacts facts, CancellationToken cancellationToken)
        {
            PartyEntries++;
            Participants = participants.ToArray();
            var layout = Layout();
            Visits = participants.ToDictionary(
                value => value.CharacterId,
                value => new DungeonRoomVisit(
                    new DungeonRunState(
                        layout,
                        layout.StartCell,
                        layout.StartRoomMapId,
                        new HashSet<MazeCell> { layout.StartCell },
                        DateTimeOffset.UnixEpoch,
                        difficulty,
                        RunId: RunId,
                        Participant: value,
                        Participants: participants,
                        EntryRevision: 1,
                        CurrentCellState: DungeonCellSeed.CompleteEmpty(
                            layout.StartCell, layout.StartRoomMapId).Snapshot()),
                    true,
                    [],
                    [],
                    1));
            foreach (var visit in Visits.Values)
                runs?.SetCurrent(visit.Run.Participant!.CharacterId, visit.Run);
            var leaderRun = Visits.Values.Single(value => value.Run.Participant!.IsLeader).Run;
            return ValueTask.FromResult<DungeonPartyEntry?>(new DungeonPartyEntry(
                layout,
                Visits,
                new DungeonEntryChange(
                    DungeonEntryChangeKind.Prepared,
                    leaderRun,
                    leaderRun.EntryRevision,
                    participants.OrderBy(value => value.Slot)
                        .Select(value => value.CharacterId).ToArray())));
        }
        public ValueTask<DungeonRoomVisit?> MoveAsync(CharacterId characterId, DungeonRunState current, MazeCell target, CancellationToken cancellationToken) => throw new NotSupportedException();

        public static DungeonLayout LayoutForTest() => Layout();

        private static DungeonLayout Layout() => new(
            DungeonId,
            0,
            2,
            1,
            new MazeCell(0, 0),
            new MazeCell(1, 0),
            61000,
            123,
            new Dictionary<MazeCell, int>
            {
                [new MazeCell(0, 0)] = 61000,
                [new MazeCell(1, 0)] = 61001,
            });
    }

    private sealed class RollbackRuns : IDungeonRunDomainService
    {
        private readonly Dictionary<CharacterId, DungeonRunState> current = [];
        public List<CharacterId> Ended { get; } = [];
        public void SetCurrent(CharacterId characterId, DungeonRunState run) =>
            current[characterId] = run;
        public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, byte difficulty) => throw new NotSupportedException();
        public DungeonRunState? Find(CharacterId characterId) => current.GetValueOrDefault(characterId);
        public bool TryAbortEntry(
            DungeonRunId expectedRunId,
            long expectedEntryRevision,
            IReadOnlyList<CharacterId> expectedRecipients)
        {
            if (expectedRecipients.Any(characterId =>
                current.GetValueOrDefault(characterId) is not { } run
                || run.RunId != expectedRunId
                || run.EntryRevision != expectedEntryRevision))
            {
                return false;
            }

            foreach (var characterId in expectedRecipients)
            {
                current.Remove(characterId);
                Ended.Add(characterId);
            }
            return true;
        }
        public DungeonRunState? TryMove(CharacterId characterId, MazeCell target) => null;
        public DungeonRunState? TryTakeGroundDrop(CharacterId characterId, ushort groundDropNumber) => null;
        public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence) => null;
        public DungeonRunState? TrySettle(CharacterId characterId) => null;
        public IReadOnlyList<MonsterDrop> AllocateMonsterDrops(CharacterId characterId, int declaredDropCount, ushort actorSequence, IReadOnlyList<RolledDrop> rolled) => [];
        public DungeonRunState? FixClearRewardCards(CharacterId characterId, ClearRewardCardFace free, ClearRewardCardFace paid, int? paidFlipCost) => null;
        public ClearRewardCardSlot? TryClaimClearRewardCard(CharacterId characterId, ClearRewardCardKind kind, int index) => null;
        public bool End(CharacterId characterId)
        {
            Ended.Add(characterId);
            return true;
        }
        public bool End(CharacterId characterId, DungeonRunId expectedRunId)
        {
            Ended.Add(characterId);
            return true;
        }
    }

    private sealed class Fatigue : ICharacterFatigueDomainService
    {
        public List<(CharacterId CharacterId, ushort Points)> Spent { get; } = [];
        public ValueTask SpendAsync(CharacterId characterId, ushort points, CancellationToken cancellationToken)
        {
            Spent.Add((characterId, points));
            return ValueTask.CompletedTask;
        }
        public ValueTask<CharacterFatigueBalance> BalanceOfAsync(CharacterId characterId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask RefundAsync(CharacterId characterId, ushort points, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnchangedState(Action? onRefresh = null) : ICharacterStateProjectionDomainService
    {
        public ValueTask<SelectCharacterResult> RefreshAsync(
            SelectCharacterResult result,
            CancellationToken cancellationToken)
        {
            onRefresh?.Invoke();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Sender(NetworkConnectionId? failRequiredConnection = null)
        : IGameMessageSender
    {
        private readonly Dictionary<NetworkConnectionId, List<IGameServerMessage>> sent = [];
        public IReadOnlyList<IGameServerMessage> Messages(NetworkConnectionId connectionId) =>
            sent.TryGetValue(connectionId, out var messages) ? messages : [];
        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken)
        {
            if (requiredDirectConnectionId == failRequiredConnection)
                throw new InvalidOperationException("required delivery failed");
            Add(requiredDirectConnectionId, delivery.RequiredDirectMessages);
            foreach (var target in delivery.BestEffortTargets)
                Add(target.ConnectionId, target.OrderedMessages);
            return ValueTask.CompletedTask;
        }
        private void Add(NetworkConnectionId connectionId, IReadOnlyList<IGameServerMessage> messages)
        {
            if (!sent.TryGetValue(connectionId, out var destination))
            {
                destination = [];
                sent.Add(connectionId, destination);
            }
            destination.AddRange(messages);
        }
    }

    private sealed class RecordingCloser : INetworkConnectionCloser
    {
        public List<NetworkConnectionId> Closed { get; } = [];

        public ValueTask RequestCloseAsync(
            NetworkConnectionId connectionId,
            CancellationToken cancellationToken)
        {
            Closed.Add(connectionId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopNotificationBus : INotificationBus
    {
        public ValueTask<NotificationDelivery> PublishAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
            where TNotification : class, INotification<TNotification> =>
            ValueTask.FromResult(new NotificationDelivery([]));
    }

    private sealed class NoopPartyTownProjection : IPartyTownProjectionDelivery
    {
        public ValueTask RefreshObservedPartiesAsync(
            IReadOnlyList<SelectedSessionIdentity> recipients,
            IReadOnlyList<SelectedSessionIdentity> representedSessions,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RefreshMovedPartyAsync(
            SelectedSessionIdentity mover,
            PartyDirectoryScope? previousScope,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class ThrowingPartyTownProjection : IPartyTownProjectionDelivery
    {
        public ValueTask RefreshObservedPartiesAsync(
            IReadOnlyList<SelectedSessionIdentity> recipients,
            IReadOnlyList<SelectedSessionIdentity> representedSessions,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RefreshMovedPartyAsync(
            SelectedSessionIdentity mover,
            PartyDirectoryScope? previousScope,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("projection failed");
    }
}
