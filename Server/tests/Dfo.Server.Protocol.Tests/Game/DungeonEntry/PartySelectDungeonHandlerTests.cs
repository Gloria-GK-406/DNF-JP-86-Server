using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Protocol.Game.Fatigue;
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
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;

public sealed class PartySelectDungeonHandlerTests
{
    [Theory]
    [InlineData(10000)]
    [InlineData(0)]
    [InlineData(10001)]
    public async Task Only_the_matching_retry_selection_keeps_the_ordinary_maze_partition(ushort selection)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new Sessions(Leader, Member);
            var runtime = new Runtime(SelectCharacterPayloadTestData.CreateComplete(), [Leader, Member]);
            var intents = new Dfo.Server.Application.World.DungeonEntryIntentDomainService();
            foreach (var member in new[] { Leader, Member })
                intents.Record(member, DungeonEntryIntent.OrdinaryRechallenge(DungeonId));
            var sender = new Sender();
            var handler = new EnterSelectDungeonHandler(new UnchangedState(), sessions,
                new RuntimeCurrentView(runtime), new PartyVisits(), intents, sender,
                TownAreaProjectionTestFactory.Create(sessions, runtime, sender),
                NullLogger<EnterSelectDungeonHandler>.Instance, new Parties(Snapshot()));
            await handler.HandleAsync(Dispatch(Leader),
                new EnterSelectDungeonRequest(GameMessageMetadata.Empty, selection), default);
            var expected = selection == DungeonId ? DungeonEntryIntent.OrdinaryRechallenge(DungeonId)
                : selection == 0 ? DungeonEntryIntent.Interactive : DungeonEntryIntent.Directed(selection);
            Assert.Equal(expected, intents.Find(Leader));
            Assert.Equal(expected, intents.Find(Member));
        });
    }

    [Fact]
    public async Task Missing_party_member_cannot_silently_open_a_partial_selection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new Sessions(Leader);
            var runtime = new Runtime(SelectCharacterPayloadTestData.CreateComplete(), [Leader]);
            var intents = new Intents();
            var sender = new Sender();
            var handler = new EnterSelectDungeonHandler(new UnchangedState(), sessions,
                new RuntimeCurrentView(runtime), new PartyVisits(), intents, sender,
                TownAreaProjectionTestFactory.Create(sessions, runtime, sender),
                NullLogger<EnterSelectDungeonHandler>.Instance, new Parties(Snapshot()));
            await handler.HandleAsync(Dispatch(Leader),
                new EnterSelectDungeonRequest(GameMessageMetadata.Empty, DungeonId), default);
            Assert.Null(intents.Consume(Leader));
            Assert.Empty(sender.Messages(Leader.ConnectionId));
        });
    }

    [Fact]
    public async Task Only_the_leader_can_return_the_frozen_dungeon_cohort_to_town()
    {
        await TestGameplay.RunAsync(async () =>
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
                NullLogger<EplpCommandHandler>.Instance);
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
        });
    }

    /// <summary>XO-2: a peer slow to take the leader's decision, within the limit, still has it before the leader's ACK.</summary>
    [Fact]
    public async Task A_slow_peer_is_delivered_the_decision_before_the_leaders_acknowledgement()
    {
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(Leader, Member);
        outbound.Transport.Slow(Member.ConnectionId, TimeSpan.FromMilliseconds(300));

        await outbound.RunAsync(async () =>
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
            var deliveries = new DungeonRunDeliveryRegistry();
            deliveries.Register(runId, [Leader, Member]);
            var handler = new EplpCommandHandler(
                sessions, runtime, new PartyPositions(), runs, deliveries, outbound.Game,
                TownAreaProjectionTestFactory.Create(sessions, runtime, outbound.Game),
                NullLogger<EplpCommandHandler>.Instance, ordering: outbound.Ordering);

            await handler.HandleAsync(Dispatch(Leader),
                new EplpCommandRequest(GameMessageMetadata.Empty, 2, 2), CancellationToken.None);
        });
        await outbound.SettledAsync();

        var decision = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == Member.ConnectionId);
        var acknowledgement = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == Leader.ConnectionId);
        Assert.IsType<EplpCommandResponse>(Assert.Single(decision.Messages));
        Assert.True(decision.Order!.Value.Sequence < acknowledgement.Order!.Value.Sequence);
        Assert.True(outbound.Delivered(Leader.ConnectionId) > outbound.Delivered(Member.ConnectionId),
            "the leader's ACK was delivered before the peer's decision");
    }

    [Theory]
    [InlineData((byte)0)] [InlineData((byte)1)] [InlineData((byte)5)]
    public async Task The_leader_opens_one_selection_context_for_every_party_member(byte leaderSlot)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new Sessions(Leader, Member);
            var runtime = new Runtime(SelectCharacterPayloadTestData.CreateComplete(), [Leader, Member]);
            var intents = new Intents();
            var sender = new Sender();
            var snapshot = Snapshot();
            snapshot = snapshot with { Members = [.. snapshot.Members.Select(x => x with
                { Slot = x.CharacterId == Leader.CharacterId ? leaderSlot : leaderSlot == 0 ? (byte)1 : (byte)0 })] };
            var handler = new EnterSelectDungeonHandler(
                new UnchangedState(),
                sessions,
                new RuntimeCurrentView(runtime),
                new PartyVisits(),
                intents,
                sender,
                TownAreaProjectionTestFactory.Create(sessions, runtime, sender),
                NullLogger<EnterSelectDungeonHandler>.Instance,
                new Parties(snapshot));

            await handler.HandleAsync(
                Dispatch(Leader),
                new EnterSelectDungeonRequest(GameMessageMetadata.Empty, DungeonId),
                CancellationToken.None);

            var expectedUsers = leaderSlot == 0 ? new[] { Leader.WireUserId, Member.WireUserId }
                : new[] { Member.WireUserId, Leader.WireUserId };
            foreach (var recipient in new[] { Leader, Member })
                Assert.Equal(leaderSlot, Assert.Single(sender.Messages(recipient.ConnectionId).OfType<UdpHostNotification>()).HostSlot);
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
        });
    }

    [Fact]
    public async Task A_follower_cannot_open_a_solo_selection_while_in_a_party()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new Sessions(Leader, Member);
            var runtime = new Runtime(SelectCharacterPayloadTestData.CreateComplete(), [Leader, Member]);
            var intents = new Intents();
            var sender = new Sender();
            var handler = new EnterSelectDungeonHandler(
                new UnchangedState(), sessions, new RuntimeCurrentView(runtime), new PartyVisits(), intents, sender,
                TownAreaProjectionTestFactory.Create(sessions, runtime, sender),
                NullLogger<EnterSelectDungeonHandler>.Instance, new Parties(Snapshot()));

            await handler.HandleAsync(
                Dispatch(Member), new EnterSelectDungeonRequest(GameMessageMetadata.Empty, DungeonId),
                CancellationToken.None);

            Assert.Empty(sender.Messages(Member.ConnectionId));
            Assert.Null(intents.Consume(Member));
            Assert.Null(intents.Consume(Leader));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_leader_cancels_selection_for_every_member_and_restores_the_shared_town(bool refusedForFatigue)
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
            .AddSingleton<ISelectedCharacterCurrentViewDomainService>(new RuntimeCurrentView(runtime))
            .AddSingleton<IOnlineCharacterRuntimeDomainService>(new PartyPositions())
            .AddSingleton<IDungeonRunDomainService>(new RollbackRuns())
            .AddSingleton(new DungeonRunDeliveryRegistry())
            .AddSingleton<IDungeonEntryIntentDomainService>(intents)
            .AddSingleton<IPartyService>(new Parties(Snapshot()))
            .AddSingleton<IGameMessageSender>(sender)
            .AddSingleton(TownAreaProjectionTestFactory.Create(sessions, runtime, sender))
            .AddSingleton<Microsoft.Extensions.Logging.ILogger<BackToVillageHandler>>(
                NullLogger<BackToVillageHandler>.Instance)
            .BuildServiceProvider();
        await TestGameplay.RunAsync(services, async () =>
        {
            if (refusedForFatigue)
            {
                var visits = new RefusedVisits();
                var selection = new SelectDungeonHandler(CurrentCharacterStub.Domain(), new UnchangedState(),
                    sessions, new RuntimeCurrentView(runtime), new Parties(Snapshot()),
                    services.GetRequiredService<IDungeonRunDomainService>(), services.GetRequiredService<DungeonRunDeliveryRegistry>(),
                    intents, visits, new RecordingQuestLogs(), new Fatigue(), sender, new RecordingCloser(),
                    services.GetRequiredService<TownAreaProjectionDelivery>(), NullLogger<SelectDungeonHandler>.Instance);
                for (var i = 0; i < 2; i++)
                    await selection.HandleAsync(Dispatch(Leader), new SelectDungeonRequest(GameMessageMetadata.Empty, DungeonId, DungeonDifficulty.Normal, 0, 0), default);
                Assert.Equal(2, visits.Attempts); // A repeated click still reaches fatigue validation.
                Assert.Equal(2, sender.Messages(Leader.ConnectionId).OfType<SelectDungeonRefusal>().Count());
                Assert.All(sender.Messages(Leader.ConnectionId).OfType<SelectDungeonRefusal>(), x => Assert.Equal(SelectDungeonRefusal.InsufficientFatigue, x.ErrorCode));
            }
            var handler = ActivatorUtilities.CreateInstance<BackToVillageHandler>(services);

            await handler.HandleAsync(
                Dispatch(Leader),
                new BackToVillageRequest(GameMessageMetadata.Empty),
                CancellationToken.None);

            Assert.IsType<BackToVillageResponse>(sender.Messages(Leader.ConnectionId).First(x => x is not SelectDungeonRefusal));
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
        });
    }

    [Fact]
    public async Task A_party_member_cannot_select_the_story_dungeon_for_the_party()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = World();
            world.Intents.Record(Member, DungeonEntryIntent.Directed(DungeonId));

            await world.Handler.HandleAsync(Dispatch(Member), Request(), CancellationToken.None);

            var refusal = Assert.IsType<SelectDungeonRefusal>(
                Assert.Single(world.Sender.Messages(Member.ConnectionId)));
            Assert.Equal(SelectDungeonRefusal.NotPartyLeader, refusal.ErrorCode);
            Assert.Equal(0, world.Visits.PartyEntries);
            Assert.Empty(world.Registry.Find(world.Visits.RunId));
        });
    }

    [Fact]
    public async Task An_unattachable_member_refuses_the_whole_party_before_a_run_is_created()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = World(memberRuntimeAvailable: false);
            world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));

            await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

            var refusal = Assert.IsType<SelectDungeonRefusal>(
                Assert.Single(world.Sender.Messages(Leader.ConnectionId)));
            Assert.Equal(SelectDungeonRefusal.SelectionCohortUnavailable, refusal.ErrorCode);
            Assert.Equal(1, refusal.Context);
            Assert.Equal(0, world.Visits.PartyEntries);
            Assert.Empty(world.Fatigue.Spent);
        });
    }

    [Fact]
    public async Task The_leader_enters_every_frozen_member_and_supplies_a_missing_selection_prelude()
    {
        await TestGameplay.RunAsync(async () =>
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
                frame => Assert.IsType<StartMapNotification>(frame),
                frame => Assert.IsType<FatigueNotification>(frame));
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
                frame => Assert.IsType<StartMapNotification>(frame),
                frame => Assert.IsType<FatigueNotification>(frame));
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
            Assert.Empty(world.Fatigue.Spent); // Charging belongs to the visit domain, not delivery.
        });
    }

    [Fact]
    public async Task An_interactive_party_selection_enters_one_shared_party_run()
    {
        await TestGameplay.RunAsync(async () =>
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
        });
    }

    [Fact]
    public async Task Entering_an_interactive_party_run_replaces_the_whole_old_party_delivery()
    {
        await TestGameplay.RunAsync(async () =>
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
        });
    }

    [Fact]
    public async Task A_generation_change_during_delivery_preparation_rolls_back_without_run_frames()
    {
        await TestGameplay.RunAsync(async () =>
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
        });
    }

    [Fact]
    public async Task A_newer_entry_revision_is_reread_and_projected_without_destroying_the_run()
    {
        await TestGameplay.RunAsync(async () =>
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
        });
    }

    [Fact]
    public async Task A_projection_failure_cannot_split_party_members_between_town_and_run()
    {
        await TestGameplay.RunAsync(async () =>
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
        });
    }

    [Fact]
    public async Task A_secondary_recipient_delivery_failure_aborts_entry_before_anyone_leaves_town()
    {
        await TestGameplay.RunAsync(async () =>
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
        });
    }

    /// <summary>
    /// DR-2, the kept branch (TASK-24 R2): the leader's batch cannot be registered (its encoding fails at the
    /// registration point), after the follower's was. The follower's START_MAP is still delivered when the job
    /// ends, so he is isolated after it, the leader too, and the entry is rolled back.
    /// </summary>
    [Fact]
    public async Task A_leader_delivery_failure_isolates_the_member_that_already_received_start_map()
    {
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(Leader, Member);
        outbound.Encoder.Fail<StartMapNotification>(
            Leader.ConnectionId, () => new InvalidOperationException("required delivery failed"));
        TestWorld? world = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => outbound.RunFromAsync(Leader.ConnectionId, async () =>
        {
            world = World(withTownProjection: true, outbound: outbound);
            world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));
            await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);
        }));
        await outbound.SettledAsync();

        var entries = outbound.Recorder.Entries.ToList();
        var memberEntry = entries.IndexOf(EntryOf(outbound, Member));
        var closes = entries.Where(entry => entry.IsClose).Select(entry => entry.Connection.ConnectionId).ToList();
        Assert.Equal([Member.ConnectionId, Leader.ConnectionId, Leader.ConnectionId], closes);
        Assert.True(memberEntry < entries.FindIndex(entry => entry.IsClose),
            "the follower is isolated after the START_MAP he was already registered");
        Assert.DoesNotContain(outbound.Messages(Leader.ConnectionId), frame => frame is StartMapNotification);
        Assert.True(await outbound.ClosedAsync(Member.ConnectionId));
        Assert.True(await outbound.ClosedAsync(Leader.ConnectionId));
        Assert.Empty(world!.Registry.Find(world.Visits.RunId));
        Assert.Equal(
            [Leader.CharacterId, Member.CharacterId],
            world.Runs.Ended.OrderBy(value => value.Value));
        Assert.True(Leader.Owns(world.Areas.Find(Leader.CharacterId)!));
        Assert.True(Member.Owns(world.Areas.Find(Member.CharacterId)!));
    }

    /// <summary>
    /// DR-2, VC-3 (TASK-24 T2): a follower whose entry fails in transport is closed by the sender alone; the
    /// handler does not see it, the run is not rolled back and the leader is entered.
    /// </summary>
    [Fact]
    public async Task A_follower_whose_transport_fails_is_closed_alone_and_the_party_still_enters()
    {
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(Leader, Member);
        outbound.Transport.Fail(Member.ConnectionId);
        TestWorld? world = null;

        await outbound.RunFromAsync(Leader.ConnectionId, async () =>
        {
            world = World(withTownProjection: true, outbound: outbound);
            world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));
            await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);
        });
        await outbound.SettledAsync();

        Assert.Equal(Dfo.Server.Abstractions.Execution.OutboundOutcome.Failed, outbound.Observer.OutcomeOf(EntryOf(outbound, Member)));
        Assert.True(await outbound.ClosedAsync(Member.ConnectionId));
        Assert.Equal(Dfo.Server.Abstractions.Execution.OutboundOutcome.Delivered, outbound.Observer.OutcomeOf(EntryOf(outbound, Leader)));
        Assert.False(outbound.Transport.IsClosed(Leader.ConnectionId));
        Assert.DoesNotContain(outbound.Recorder.Entries, entry => entry.IsClose);
        Assert.Empty(world!.Runs.Ended);
        Assert.Equal(2, world.Registry.Find(world.Visits.RunId).Count);
    }

    /// <summary>XO-1: a follower slow within the limit is entered before the requesting leader.</summary>
    [Fact]
    public async Task A_slow_follower_is_delivered_its_entry_before_the_requesting_leader()
    {
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(Leader, Member);
        outbound.Transport.Slow(Member.ConnectionId, TimeSpan.FromMilliseconds(300));
        await outbound.RunAsync(async () =>
        {
            var world = World(outbound: outbound);
            world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));
            await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);
        });
        await outbound.SettledAsync();

        var follower = EntryOf(outbound, Member);
        var leader = EntryOf(outbound, Leader);
        Assert.True(follower.Order!.Value.Sequence < leader.Order!.Value.Sequence);
        Assert.True(outbound.Delivered(Leader.ConnectionId) > outbound.Delivered(Member.ConnectionId),
            "the leader was entered before the follower");
    }

    /// <summary>XO-1, VC-3: a follower who cannot take his entry in time is closed alone; the leader is still entered.</summary>
    [Fact]
    public async Task A_follower_over_the_limit_is_closed_and_the_requesting_leader_is_still_entered()
    {
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery(
            Dfo.Server.Networking.Outbound.OutboundSendOptions.Default with { DeliveryTimeout = TimeSpan.FromMilliseconds(300) });
        outbound.Connect(Leader, Member);
        outbound.Transport.Stall(Member.ConnectionId);
        await outbound.RunAsync(async () =>
        {
            var world = World(outbound: outbound);
            world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));
            await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);
        });
        await outbound.SettledAsync();

        Assert.Equal(Dfo.Server.Abstractions.Execution.OutboundOutcome.TimedOut, outbound.Observer.OutcomeOf(EntryOf(outbound, Member)));
        Assert.True(outbound.Transport.IsClosed(Member.ConnectionId));
        Assert.Equal(Dfo.Server.Abstractions.Execution.OutboundOutcome.Delivered, outbound.Observer.OutcomeOf(EntryOf(outbound, Leader)));
    }

    /// <summary>
    /// XO-1 ends with the requester's batch: what the town departure sends next, to people still in town,
    /// is not held behind a slow follower (it is in no ordered group).
    /// </summary>
    [Fact]
    public async Task The_town_departure_after_a_party_entry_is_not_in_the_entry_group()
    {
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        var bystander = new NetworkConnectionId("bystander");
        outbound.Connect(Leader, Member);
        outbound.Connect(bystander);
        await outbound.RunAsync(async () =>
        {
            var world = World(partyTownProjection: new BystanderTownProjection(outbound.Game, bystander), outbound: outbound);
            world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));
            await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);
        });
        await outbound.SettledAsync();

        Assert.NotNull(EntryOf(outbound, Member).Order);
        Assert.NotNull(EntryOf(outbound, Leader).Order);
        var departures = outbound.Recorder.Entries.Where(entry => entry.Connection.ConnectionId == bystander).ToList();
        Assert.NotEmpty(departures);
        Assert.All(departures, entry => Assert.Null(entry.Order));
    }

    /// <summary>A party projection that tells someone still in town that a member left it.</summary>
    private sealed class BystanderTownProjection(IGameMessageSender game, NetworkConnectionId bystander)
        : IPartyTownProjectionDelivery
    {
        public ValueTask RefreshObservedPartiesAsync(
            IReadOnlyList<SelectedSessionIdentity> recipients,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RefreshMovedPartyAsync(
            SelectedSessionIdentity mover,
            PartyDirectoryScope? previousScope,
            CancellationToken cancellationToken) =>
            game.SendNotiAsync(bystander, new SupportTagNotification(), cancellationToken);
    }

    /// <summary>The batch that carries the session's START_MAP.</summary>
    private static Dfo.Server.Abstractions.Execution.OutboundEntry EntryOf(
        Dfo.Server.Protocol.Tests.Outbox.XoDelivery outbound, SelectedSessionIdentity session) =>
        outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == session.ConnectionId
            && entry.Messages.Any(message => message is StartMapNotification));

    private static TestWorld World(
        bool memberRuntimeAvailable = true,
        bool replaceMemberDuringPrelude = false,
        bool advanceEntryRevisionDuringPrelude = false,
        bool withTownProjection = false,
        IPartyTownProjectionDelivery? partyTownProjection = null,
        NetworkConnectionId? failRequiredConnection = null,
        Dfo.Server.Protocol.Tests.Outbox.XoDelivery? outbound = null)
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
        IGameMessageSender game = outbound?.Game ?? sender;
        var closer = new RecordingCloser();
        INetworkConnectionCloser closing = outbound?.Closer ?? closer;
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
            game,
            SharedTownMovementDomainService.Instance);
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
            new RuntimeCurrentView(runtime),
            new Parties(Snapshot()),
            runs,
            registry,
            intents,
            visits,
            new RecordingQuestLogs(),
            fatigue,
            game,
            closing,
            townProjection,
            NullLogger<SelectDungeonHandler>.Instance,
            entryProjection: outbound is null ? null : new DungeonEntryProjectionDelivery(
                CurrentCharacterStub.Domain(), state, sessions, new RuntimeCurrentView(runtime), runs, registry,
                intents, visits, fatigue, game, closing, townProjection,
                NullLogger<DungeonEntryProjectionDelivery>.Instance, ordering: outbound.Ordering));
        return new TestWorld(
            handler, visits, intents, registry, runs, fatigue, sender, closer, areas);
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
    private const DungeonDifficulty Difficulty = (DungeonDifficulty)3;

    private sealed record TestWorld(
        SelectDungeonHandler Handler,
        PartyVisits Visits,
        Intents Intents,
        DungeonRunDeliveryRegistry Registry,
        RollbackRuns Runs,
        Fatigue Fatigue,
        Sender Sender,
        RecordingCloser Closer,
        ITownAreaDomainService Areas);

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
        public PartyOperationResult RemoveMember(CharacterId currentLeaderCharacterId, CharacterId targetCharacterId) => throw new NotSupportedException();
        public PartyOperationResult LeaveParty(CharacterId characterId) => throw new NotSupportedException();
        public PartyOperationResult CleanupCharacter(CharacterId characterId) => throw new NotSupportedException();
    }

    private sealed class Sessions : ISelectedSessionDomainService
    {
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

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
        IReadOnlyList<SelectedSessionIdentity> available) : ILegacySessionSnapshotFixture
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
        : ILegacySessionSnapshotFixture
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

    private sealed class PartyPositions : IOnlineCharacterRuntimeDomainService
    {
        public CharacterPositionState? PositionOf(CharacterId characterId) =>
            characterId == Leader.CharacterId
                ? new CharacterPositionState(1, 1, 400, 300, 1)
                : characterId == Member.CharacterId
                    ? new CharacterPositionState(1, 1, 430, 300, 3)
                    : null;

        public void Begin(SelectedSessionIdentity session, CharacterPositionState position, byte hpPercent) =>
            throw new NotSupportedException();
        public CharacterPositionState CrossInto(
            SelectedSessionIdentity session, byte townId, byte areaId, short x, short y) =>
            throw new NotSupportedException();
        public CharacterPositionState StepTo(
            SelectedSessionIdentity session, short x, short y, byte direction) =>
            throw new NotSupportedException();
        public bool EndExact(SelectedSessionIdentity session) => throw new NotSupportedException();

        // Test double: only the position is modelled; the rest of the runtime is empty.
        public CharacterPositionState? PositionOf(SelectedSessionIdentity session) => PositionOf(session.CharacterId);
        public byte HpPercentOf(CharacterId characterId) => 0;
        public bool IsCoolingDown(CharacterId characterId, Dfo.Server.Abstractions.Inventory.ItemCooldownKey key) => false;
        public void StartCooldown(SelectedSessionIdentity session, Dfo.Server.Abstractions.Inventory.ItemCooldownKey key, TimeSpan duration) { }
        public void ApplySpiritPotion(SelectedSessionIdentity session, double bonusRate, TimeSpan duration) { }
        public double SpiritPotionBonusRateOf(CharacterId characterId) => 0;
    }

    private sealed class RefusedVisits : IDungeonVisitDomainService
    {
        public int Attempts { get; private set; }
        public bool DefersSelectionState(byte level, bool tutorialCompleted = false) => false;
        public ValueTask<DungeonEntryAttempt> TryEnterPartyAsync(IReadOnlyList<DungeonRunParticipant> members, int dungeon, DungeonEntryFacts facts, CancellationToken token)
        {
            Attempts++;
            return ValueTask.FromResult(new DungeonEntryAttempt(null, DungeonEntryFailure.InsufficientFatigue, 0));
        }
        public ValueTask<Dfo.Server.Abstractions.World.DungeonEntry?> EnterAsync(CharacterId id, int dungeon, DungeonDifficulty difficulty, DungeonEntryFacts facts, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<DungeonRoomVisit?> MoveAsync(CharacterId id, DungeonRunState state, MazeCell cell, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class Intents : IDungeonEntryIntentDomainService
    {
        private readonly Dictionary<SelectedSessionIdentity, DungeonEntryIntent> values = [];
        public void Record(SelectedSessionIdentity session, DungeonEntryIntent intent) => values[session] = intent;
        public DungeonEntryIntent? Consume(SelectedSessionIdentity session) =>
            values.Remove(session, out var value) ? value : null;
        public void RestoreIfAbsent(SelectedSessionIdentity session, DungeonEntryIntent intent) => values.TryAdd(session, intent);
        public bool ClearExact(SelectedSessionIdentity session) => values.Remove(session);
    }

    private sealed class PartyVisits(RollbackRuns? runs = null) : IDungeonVisitDomainService
    {
        public int PartyEntries { get; private set; }
        public DungeonRunId RunId { get; } = DungeonRunId.New();
        public IReadOnlyList<DungeonRunParticipant> Participants { get; private set; } = [];
        public IReadOnlyDictionary<CharacterId, DungeonRoomVisit> Visits { get; private set; } =
            new Dictionary<CharacterId, DungeonRoomVisit>();

        public bool DefersSelectionState(byte characterLevel, bool tutorialCompleted = false) => false;
        public ValueTask<EnteredDungeon?> EnterAsync(CharacterId characterId, int dungeonId, DungeonDifficulty difficulty, DungeonEntryFacts facts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<DungeonPartyEntry?> EnterPartyAsync(IReadOnlyList<DungeonRunParticipant> participants, int dungeonId, DungeonDifficulty difficulty, DungeonEntryFacts facts, CancellationToken cancellationToken)
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
            new Dictionary<MazeCell, DungeonCellDefinition> {
                [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(61000),
                [new MazeCell(1, 0)] = DungeonCellDefinition.Fixed(61001),
            });
    }

    private sealed class RollbackRuns : IDungeonRunDomainService
    {
        private readonly Dictionary<CharacterId, DungeonRunState> current = [];
        public List<CharacterId> Ended { get; } = [];
        public void SetCurrent(CharacterId characterId, DungeonRunState run) =>
            current[characterId] = run;
        public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, DungeonDifficulty difficulty) => throw new NotSupportedException();
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
        public ValueTask<CharacterFatigueBalance> BalanceOfAsync(CharacterId characterId, CancellationToken cancellationToken) => ValueTask.FromResult(new CharacterFatigueBalance(188, 1));
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

    /// <summary>
    /// What the handler registered, per connection, read from the outbox of a real registration adapter; every call
    /// goes to the real protocol sender over it (TASK-25). <paramref name="failRequiredConnection"/>'s direct messages
    /// fail to encode where they are registered, with the exception the old double threw; a CMD or NOTI sent outside
    /// an explicit delivery fails the same way, as the old double refused one.
    /// </summary>
    private sealed class Sender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();

        public Sender(NetworkConnectionId? failRequiredConnection = null)
        {
            outbound.RefuseOutsideExplicit();
            if (failRequiredConnection is { } connection)
                outbound.FailDirect(connection, () => new InvalidOperationException("required delivery failed"));
        }

        public IReadOnlyList<IGameServerMessage> Messages(NetworkConnectionId connectionId) =>
            [.. outbound.MessagesFor(connectionId).Cast<IGameServerMessage>()];
        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
            outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }

    /// <summary>The closes a real closer registered, read from its outbox (TASK-25).</summary>
    private sealed class RecordingCloser : INetworkConnectionCloser
    {
        private readonly RecordedOutbound outbound = new();

        public List<NetworkConnectionId> Closed => [.. outbound.Closes];

        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken ct) =>
            outbound.Closer.RequestCloseExactAsync(expected, ct);
        public ValueTask RequestCloseAsync(
            NetworkConnectionId connectionId,
            CancellationToken cancellationToken) => outbound.Closer.RequestCloseAsync(connectionId, cancellationToken);
    }

    private sealed class NoopNotificationBus : Dfo.Server.Testing.ProjectionEventService
    {
        public override ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new NotificationDelivery([]));
    }

    private sealed class NoopPartyTownProjection : IPartyTownProjectionDelivery
    {
        public ValueTask RefreshObservedPartiesAsync(
            IReadOnlyList<SelectedSessionIdentity> recipients,
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
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RefreshMovedPartyAsync(
            SelectedSessionIdentity mover,
            PartyDirectoryScope? previousScope,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("projection failed");
    }
}
