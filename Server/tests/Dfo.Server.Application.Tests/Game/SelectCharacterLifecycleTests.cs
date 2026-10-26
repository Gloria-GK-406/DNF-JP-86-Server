using System.Diagnostics.CodeAnalysis;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.Login;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Game;

public sealed partial class SelectCharacterLifecycleTests
{
    [Fact]
    public async Task Tau_current_period_is_rechecked_after_publication_even_when_scheduled_snapshot_missed_selection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var f = await Fixture.CreateAsync(tau: new TauMarker());
            await f.Handler.HandleAsync(f.Context, f.Command, default);
            var types = f.Bus.DispatchedCommands;
            Assert.True(types.IndexOf(typeof(RefillTauStage))
                > types.IndexOf(typeof(PublishSelectedCharacterSessionStage)));
            Assert.True(types.IndexOf(typeof(RefillTauStage))
                < types.IndexOf(typeof(SynchronizeSelectedCharacterQuestsStage)));
        });
    }
    private sealed class TauMarker : ITauKingdomDomainService
    {
        public bool Supports(int dungeonId) => dungeonId == 3700;
        public ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> RefillOnlineTicketAsync(SelectedSessionIdentity session, CancellationToken ct) => throw new NotSupportedException();
    }
    [Fact]
    public async Task Success_OrdersRequiredDeliveryBeforePublicationAndPostEntryStages()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync();

            await fixture.Handler.HandleAsync(
                fixture.Context, fixture.Command, CancellationToken.None);
            var result = fixture.Bus.State!;

            var spawn = fixture.Result.World.Channel.Spawn;
            Assert.Equal(
                new CharacterPositionState(spawn.TownId, spawn.AreaId, spawn.X, spawn.Y, spawn.Direction),
                result.Character.Position);
            Assert.Equal(result.Character.Position, fixture.Mutations.PersistedPosition);
            // The refill runs and its delta goes out before the post-marker frames, not after. That
            // order is on the wire: the reference session's one UPDATE_ITEM_LIST sits directly after
            // USER_POSITION, ahead of the invisibility flags, the clone title, the roster payload and
            // the honour level.
            Assert.Equal(
                ["prepare", "position", "static", "inventory", "pre", "active", "publish",
                    "plan-pvp", "x11", "quest", "pet", "premium"],
                fixture.Ledger);
            Assert.Equal(
                [
                    typeof(PrepareSelectedCharacterSessionStage),
                    typeof(CleanupExpiredCharacterItemsStage),
                    typeof(EnsureDailyContractPuppetsStage),
                    typeof(InitializeHousingStage),
                    typeof(PrepareSelectedCharacterStateStage),
                    typeof(ApplySelectedCharacterDailyRefillStage),
                    typeof(PublishSelectedCharacterSessionStage),
                    typeof(SynchronizeSelectedCharacterQuestsStage),
                    typeof(ReviveSelectedCharacterPetStage),
                ],
                fixture.Bus.DispatchedCommands);
            Assert.NotNull(fixture.Directory.Find(fixture.AccountId));
            Assert.Equal(
                result.Character.Identity.CharacterId,
                fixture.Users.GetRequired(fixture.OnlineUserId).CurrentCharacterId);
            Assert.Empty(fixture.Closer.Closed);
        });
    }

    /// <summary>
    /// A refill that committed something puts the bag change on the wire, in its own place.
    /// </summary>
    /// <remarks>
    /// The frame is the one a cleared run's card reward now sends too, and it was moved out of the
    /// state-synchronization slice so that both may send it. This is the entry path's half of that:
    /// it still sends it, still after the refill and still before the post-marker tail. The
    /// fixture's ordinary refill commits nothing, which is why this asks for one that does.
    /// </remarks>
    [Fact]
    public async Task CommittedRefill_SendsTheBagChangeBetweenTheRefillAndTheTail()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(refillCommits: true);

            await fixture.Handler.HandleAsync(
                fixture.Context, fixture.Command, CancellationToken.None);

            Assert.Equal(
                ["prepare", "position", "static", "inventory", "delta", "pre", "active", "publish",
                    "plan-pvp", "x11", "quest", "pet", "premium"],
                fixture.Ledger);
        });
    }

    [Fact]
    public async Task RequiredOwnerFailure_CompensatesExactAttemptAndClosesOnlyItsConnection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(failTrailingLocks: true);

            await Assert.ThrowsAsync<IOException>(async () =>
                await fixture.Handler.HandleAsync(
                    fixture.Context, fixture.Command, CancellationToken.None));

            Assert.Equal(
                ["prepare", "position", "static", "inventory", "pre", "active", "publish",
                    "plan-pvp", "x11", "cleanup", "close"],
                fixture.Ledger);
            Assert.Null(fixture.Directory.Find(fixture.AccountId));
            Assert.Null(fixture.Users.GetRequired(fixture.OnlineUserId).CurrentCharacterId);
            Assert.Equal([fixture.ConnectionId], fixture.Closer.Closed);
        });
    }

    /// <summary>
    /// DR-1, the kept workflow branch (TASK-24 R1): the trailing locks cannot be registered for the requester (their
    /// encoding fails at the registration point, where the transitional double threw). The exact attempt is
    /// compensated and the connection is closed after the entries already registered, as before the outbox.
    /// </summary>
    [Fact]
    public async Task An_entry_message_that_cannot_be_registered_compensates_and_closes_the_requester()
    {
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        var fixture = await Fixture.CreateAsync(
            gameSender: outbound.Game, connectionCloser: outbound.Closer);
        outbound.Connect(fixture.Context.Connection);
        outbound.Encoder.Fail(fixture.ConnectionId,
            message => message.GetType().Name == "TrailingEquipmentLockSnapshotMessage",
            () => new IOException("required delivery failed"));

        await Assert.ThrowsAsync<IOException>(() => outbound.RunAsync(() =>
            fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None).AsTask()));
        await outbound.SettledAsync();

        Assert.Contains("cleanup", fixture.Ledger);
        Assert.Null(fixture.Directory.Find(fixture.AccountId));
        Assert.True(outbound.CloseRegistered(fixture.ConnectionId));
        var entries = outbound.Recorder.Entries.ToList();
        Assert.True(entries.FindIndex(entry => !entry.IsClose) < entries.FindIndex(entry => entry.IsClose),
            "the accepted entry registered before the failure goes out before the close");
        Assert.True(await outbound.ClosedAsync(fixture.ConnectionId));
    }

    /// <summary>
    /// DR-1 (TASK-24 T1): the entry is registered whole and the CMD succeeds; its delivery then fails in transport.
    /// The sender closes the connection and nothing is compensated here: the session is torn down by the
    /// connection's own disconnect CMD (TASK-23 W5 and N9 on the wire).
    /// </summary>
    [Fact]
    public async Task An_entry_whose_delivery_fails_in_transport_is_closed_by_the_sender_without_compensation()
    {
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        var fixture = await Fixture.CreateAsync(
            gameSender: outbound.Game, connectionCloser: outbound.Closer);
        outbound.Connect(fixture.Context.Connection);
        outbound.Transport.Fail(fixture.ConnectionId);

        await outbound.RunAsync(() =>
            fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None).AsTask());
        await outbound.SettledAsync();

        Assert.DoesNotContain("cleanup", fixture.Ledger);
        Assert.NotNull(fixture.Directory.Find(fixture.AccountId));
        Assert.DoesNotContain(outbound.Recorder.Entries, entry => entry.IsClose);
        Assert.Contains(outbound.Outcomes(fixture.ConnectionId), outcome => outcome == Dfo.Server.Abstractions.Execution.OutboundOutcome.Failed);
        Assert.True(await outbound.ClosedAsync(fixture.ConnectionId));
    }

    /// <summary>
    /// AC-011, the third wire-level case, at the sender level (TASK-24 ruling 3; on the characterization host the
    /// requester always lands on the non-shared town gate, so no peer could be told without a production change).
    /// A selection fails after its publication; the compensation's cleanup plan reaches the real cleanup delivery,
    /// the peer's cleanup notice is registered, and the requester is closed after it.
    /// </summary>
    [Fact]
    public async Task A_selection_compensated_after_publication_registers_the_peers_cleanup_notice_before_the_close()
    {
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        var peer = SelectedSessionIdentity.Create(
            new AccountId(777), new CharacterId(4243), new NetworkConnectionId("Game:peer"), 1, 10011);
        var cleanupDelivery = (ICleanupNotificationDelivery)Activator.CreateInstance(
            typeof(Dfo.Server.Protocol.ProtocolModule).Assembly.GetType(
                "Dfo.Server.Protocol.Game.StateSynchronization.CleanupNotificationDelivery", throwOnError: true)!,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
            binder: null, args: [outbound.Game, outbound.Projections], culture: null)!;
        var fixture = await Fixture.CreateAsync(failQuest: true,
            gameSender: outbound.Game, connectionCloser: outbound.Closer, cleanupDelivery: cleanupDelivery);
        fixture.Cleanup.Plan = session => new SelectedSessionCleanupNotificationPlan(
            session.ConnectionId, [], [peer],
            [new SelectedSessionCleanupNotice(SelectedSessionCleanupNoticeKind.TownDeparture, session.WireUserId)]);
        outbound.Connect(fixture.Context.Connection);
        outbound.Connect(peer.ConnectionId);

        await Assert.ThrowsAsync<IOException>(() => outbound.RunAsync(() =>
            fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None).AsTask()));
        await outbound.SettledAsync();

        Assert.Contains("cleanup", fixture.Ledger);
        var entries = outbound.Recorder.Entries.ToList();
        var notice = entries.FindIndex(entry => entry.Connection.ConnectionId == peer.ConnectionId
            && entry.Messages.Any(message => message is Dfo.Server.Protocol.Tests.Outbox.XoProjected { Notification: "SessionCleanupAnnounced" }));
        var close = entries.FindIndex(entry => entry.IsClose && entry.Connection.ConnectionId == fixture.ConnectionId);
        Assert.True(notice >= 0, "the peer's cleanup notice was registered");
        Assert.True(close > notice, "the requester is closed after the peer is told");
        Assert.Equal(Dfo.Server.Abstractions.Execution.OutboundOutcome.Delivered, outbound.Observer.OutcomeOf(entries[notice]));
        Assert.False(outbound.Transport.IsClosed(peer.ConnectionId));
        Assert.True(await outbound.ClosedAsync(fixture.ConnectionId));
    }

    [Fact]
    public async Task StaticFailure_DoesNotInvokeDailyRefillMutation()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(failStatic: true);

            await Assert.ThrowsAsync<IOException>(async () =>
                await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None));

            Assert.Equal(["prepare", "position", "static", "cleanup", "close"], fixture.Ledger);
            Assert.Null(fixture.Directory.Find(fixture.AccountId));
            Assert.Equal(0, fixture.Inventory.Claims);

            fixture.Sender.FailStatic = false;
            await fixture.Handler.HandleAsync(
                fixture.Context,
                fixture.Command,
                CancellationToken.None);
            Assert.Equal(1, fixture.Inventory.Claims);
        });
    }

    [Fact]
    public async Task ExpiredItemCleanupRefusal_RejectsBeforeCharacterSnapshotAndCompensates()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(refuseExpiredCleanup: true);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None));

            Assert.Equal(["prepare", "x08", "cleanup", "close"], fixture.Ledger);
            Assert.Equal(0, fixture.Sender.StateMessageCount);
            Assert.Equal(0, fixture.Inventory.Claims);
            Assert.Null(fixture.Directory.Find(fixture.AccountId));
            Assert.DoesNotContain(typeof(PrepareSelectedCharacterStateStage), fixture.Bus.DispatchedCommands);
        });
    }

    [Fact]
    public async Task ExpiredItemCleanupException_RejectsBeforeCharacterSnapshotAndCompensates()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(failExpiredCleanup: true);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None));

            Assert.IsType<IOException>(error.InnerException);
            Assert.Equal(["prepare", "x08", "cleanup", "close"], fixture.Ledger);
            Assert.Equal(0, fixture.Sender.StateMessageCount);
            Assert.Null(fixture.Directory.Find(fixture.AccountId));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingRequiredPrePublicationState_EmitsNoAcceptedOutput(
        bool missingRoster)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(
                missingRoster: missingRoster,
                missingSkillMirror: !missingRoster);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await fixture.Handler.HandleAsync(
                    fixture.Context, fixture.Command, CancellationToken.None));

            Assert.Equal(0, fixture.Sender.StateMessageCount);
            Assert.DoesNotContain("pre", fixture.Ledger);
            Assert.Equal(["prepare", "position", "cleanup", "close"], fixture.Ledger);
        });
    }

    [Fact]
    public async Task QuestAndPetChangesRemainInDeliveryWithoutReplacingASessionSnapshot()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(changeQuest: true, revivePet: true);

            await fixture.Handler.HandleAsync(
                fixture.Context, fixture.Command, CancellationToken.None);
            var result = fixture.Bus.State!;

            Assert.Equal(3, Assert.Single(result.CharacterEntry.Quests.ActiveQuests).TriggerValue);
            Assert.Equal(1, Assert.Single(result.CharacterEntry.Features.Pets).Field04);
            Assert.NotNull(fixture.SessionEntry.RuntimeResult);
            Assert.DoesNotContain("runtime-update", fixture.Ledger);
        });
    }

    [Fact]
    public async Task RejectedRepeatedLoginStillLetsDisconnectObserverCleanupTheSelectedSession()
    {
        var fixture = await Fixture.CreateAsync();
        await TestGameplay.RunAsync(async () =>
        {
            await fixture.Handler.HandleAsync(
                fixture.Context, fixture.Command, CancellationToken.None);
        });
        var selected = TestGameplay.Run(() =>
        {
            var selected = Assert.IsType<SelectedSessionIdentity>(
                fixture.Directory.FindByConnection(fixture.ConnectionId));
            return selected;
        });
        var sendsBeforeRejection = fixture.Sender.StateMessageCount;
        await TestGameplay.RunAsync(async () =>
        {
            var login = GameLoginTests.CreateHandler(
                fixture.Users,
                fixture.Sender,
                new RejectedLoginAuthentication(),
                new UnusedAccountEntryStateDomainService(),
                fixture.Closer,
                NullLogger<GameLoginCommandHandler>.Instance);

            await login.HandleAsync(
                fixture.Context,
                new GameLoginRequest(
                    GameMessageMetadata.Empty,
                    "lifecycle",
                    "already-consumed-ticket-cannot-be-replayed-0"),
                CancellationToken.None);
        });

        TestGameplay.Run(() =>
        {
            Assert.False(fixture.Users.TryGetByConnection(fixture.ConnectionId, out _));
            Assert.Equal(selected, fixture.Directory.FindByConnection(fixture.ConnectionId));
            Assert.Equal(sendsBeforeRejection, fixture.Sender.StateMessageCount);
            Assert.Contains(fixture.ConnectionId, fixture.Closer.Closed);
        });

        var observer = new OnlineUserConnectionObserver(
            fixture.Users,
            fixture.Directory,
            fixture.Cleanup,
            new StubChannelEntryPolicy(),
            new StubServerConfigService(),
            fixture.Sender,
            new NoopCleanupDelivery(),
            new TestEffectFootprints.LifecycleSource([]), TimeProvider.System);
        await TestGameplay.RunAsync(async () =>
        {
        await observer.DisconnectedAsync(
            fixture.Context.Connection,
            CancellationToken.None);
        });

        TestGameplay.Run(() =>
        {
            var cleanup = Assert.Single(fixture.Cleanup.Calls);
            Assert.Equal(selected, cleanup.Session);
            Assert.False(cleanup.IncludeOwner);
            Assert.Null(fixture.Directory.FindByConnection(fixture.ConnectionId));
            Assert.Null(fixture.Directory.Find(fixture.AccountId));
        });
    }

    [Fact]
    public async Task AdmissionRefusal_SendsOrderedX08AndHasZeroLifecycleSideEffects()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(refuseAdmission: true);

            await Assert.ThrowsAsync<Dfo.Server.Data.Channel.ChannelAdmissionRefusedException>(async () =>
                await fixture.Handler.HandleAsync(
                    fixture.Context, fixture.Command, CancellationToken.None));

            Assert.Equal(["prepare", "x08"], fixture.Ledger);
            Assert.Null(fixture.Directory.Find(fixture.AccountId));
            Assert.Null(fixture.Users.GetRequired(fixture.OnlineUserId).CurrentCharacterId);
            Assert.Empty(fixture.Closer.Closed);
        });
    }

    [Fact]
    public async Task AdmissionRefusal_DoesNotSaveOrReleaseExistingDirtyLease()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(refuseAdmission: true);
            var previous = SelectedSessionIdentity.Create(
                fixture.AccountId, fixture.Result.Character.Identity.CharacterId,
                fixture.ConnectionId, fixture.AuthenticatedGeneration,
                StubChannelEntryPolicy.GeneralChannel.ListenerPort);
            fixture.Directory.Select(previous);

            await Assert.ThrowsAsync<Dfo.Server.Data.Channel.ChannelAdmissionRefusedException>(async () =>
                await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None));

            Assert.Equal(0, fixture.Inventory.Saves);
            Assert.Equal(previous, fixture.Directory.Find(fixture.AccountId));
            Assert.Empty(fixture.Cleanup.Calls);
        });
    }

    [Fact]
    public async Task UnrepresentableWireIdentity_HasZeroRegisteredOrDurablePreparationSideEffects()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(invalidWireIdentity: true);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None));

            Assert.Equal(0, fixture.Preparation!.FullPreparationCalls);
            Assert.Equal(["prepare"], fixture.Ledger);
            Assert.Null(fixture.Directory.Find(fixture.AccountId));
            Assert.Empty(fixture.Cleanup.Calls);
            Assert.Empty(fixture.Closer.Closed);
        });
    }

    [Fact]
    public async Task FullPreparationFailure_AfterRegistrationCompensatesExactSessionAndCloses()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(failPreparation: true);

            await Assert.ThrowsAsync<IOException>(async () =>
                await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None));

            Assert.Equal(1, fixture.Preparation!.FullPreparationCalls);
            Assert.Equal(["prepare", "cleanup", "close"], fixture.Ledger);
            Assert.Null(fixture.Directory.Find(fixture.AccountId));
            Assert.Equal([fixture.ConnectionId], fixture.Closer.Closed);
        });
    }

    [Fact]
    public async Task PvpDirectFailure_DoesNotMarkReadyAndRollsBackPublishedAttempt()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(planPvp: true, failPvp: true);

            await Assert.ThrowsAsync<IOException>(async () =>
                await fixture.Handler.HandleAsync(
                    fixture.Context, fixture.Command, CancellationToken.None));

            Assert.Equal(
                ["prepare", "position", "static", "inventory", "pre", "active", "publish",
                    "plan-pvp", "pvp", "cleanup", "close"],
                fixture.Ledger);
            Assert.DoesNotContain("ready", fixture.Ledger);
            Assert.Null(fixture.Directory.Find(fixture.AccountId));
            Assert.Null(fixture.Users.GetRequired(fixture.OnlineUserId).CurrentCharacterId);
        });
    }

    [Fact]
    public async Task Pvp_plan_publishes_the_newcomer_to_its_audience_and_the_selection_completes_every_stage()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(planPvp: true, failPvpTarget: true);

            await fixture.Handler.HandleAsync(
                fixture.Context, fixture.Command, CancellationToken.None);

            Assert.Equal(
                ["prepare", "position", "static", "inventory", "pre", "active", "publish",
                    "plan-pvp", "pvp", "ready", "target", "x11", "quest", "pet", "premium"],
                fixture.Ledger);
            Assert.NotNull(fixture.Directory.Find(fixture.AccountId));
            Assert.Empty(fixture.Closer.Closed);

            // Who was told, and what about. The fan-out used to be a list of connections built by the
            // domain service; it is the same people and the same subject, said in characters.
            var (audience, notification) = Assert.Single(fixture.Notifications.Published);
            Assert.Equal(
                [new CharacterId(4242)],
                Assert.IsType<NotificationAudience.TheseCharacters>(audience).CharacterIds);
            // Built from the state that was published, not from the one this fixture started with:
            // spawn placement moves the character between the two, and asserting against the earlier
            // value is how a first attempt at this case managed to fail while the code was right.
            Assert.Equal(
                fixture.SessionEntry.RuntimeResult!.Character,
                Assert.IsType<PvpParticipantPublished>(notification).Newcomer.Character);
        });
    }

    [Fact]
    public async Task EnteringAnOccupiedTown_PublishesTheNewcomerToTheExistingCharacters()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(planTown: true);

            await fixture.Handler.HandleAsync(
                fixture.Context, fixture.Command, CancellationToken.None);

            var (audience, notification) = Assert.Single(fixture.Notifications.Published);
            Assert.Equal(
                [new CharacterId(4243)],
                Assert.IsType<NotificationAudience.TheseSessions>(audience).Sessions
                    .Select(value => value.CharacterId));
            Assert.Equal("TownParticipantPublished", notification.GetType().Name);

            var partySteps = fixture.Ledger
                .Where(value => value.StartsWith("party:", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(["party:501", "party:4243"], partySteps);
            var firstParty = fixture.Ledger.IndexOf(partySteps[0]);
            Assert.Equal("pre", fixture.Ledger[firstParty - 1]);
            Assert.Equal("target", fixture.Ledger[firstParty + 1]);
            Assert.Equal(partySteps[1], fixture.Ledger[firstParty + 2]);
        });
    }

    [Fact]
    public async Task TransientTownPublicationFailureRetriesBeforeSendingPartyState()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync(
                planTown: true,
                failFirstTownPublication: true);

            await fixture.Handler.HandleAsync(
                fixture.Context, fixture.Command, CancellationToken.None);

            Assert.Equal(
                ["party:501", "party:4243"],
                fixture.Ledger
                    .Where(value => value.StartsWith("party:", StringComparison.Ordinal)));
            Assert.Equal(2, fixture.Notifications.Published.Count);
        });
    }

    [Fact]
    public async Task SameConnectionSwitch_RefreshesGenerationAfterNormalCleanup()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync();
            Assert.True(fixture.Users.TryGetByConnection(fixture.ConnectionId, out var before));
            var old = SelectedSessionIdentity.Create(
                fixture.AccountId, new CharacterId(500), fixture.ConnectionId,
                before!.Generation, StubChannelEntryPolicy.GeneralChannel.ListenerPort);
            fixture.Directory.Select(old);
            fixture.Active.EnterWorld(old);
            fixture.Ledger.Clear();
            Assert.True(fixture.Users.TryGetByConnection(fixture.ConnectionId, out var selected));

            await fixture.Handler.HandleAsync(
                fixture.Context, fixture.Command, CancellationToken.None);

            var current = fixture.Directory.Find(fixture.AccountId)!;
            Assert.True(current.ConnectionGeneration > selected!.Generation);
            Assert.Equal([(old, true)], fixture.Cleanup.Calls);
            Assert.DoesNotContain(fixture.ConnectionId, fixture.Closer.Closed);
            Assert.False(fixture.Directory.ReleaseExact(old));
        });
    }

    [Fact]
    public async Task CrossConnectionDisplacement_CleansCurrentOwnerBeforeRegisteringReplacement()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync();
            var displaced = SelectedSessionIdentity.Create(
                fixture.AccountId, new CharacterId(500), new NetworkConnectionId("Game:old"),
                1, StubChannelEntryPolicy.GeneralChannel.ListenerPort);
            fixture.Directory.Select(displaced);

            await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal([(displaced, false)], fixture.Cleanup.Calls);
            Assert.Equal([displaced], fixture.Cleanup.SelectedOwnersAtCall);
            Assert.Contains(displaced.ConnectionId, fixture.Closer.Closed);
            Assert.Equal(fixture.ConnectionId, fixture.Directory.Find(fixture.AccountId)!.ConnectionId);
        });
    }

    [Fact]
    public async Task DisplacedCleanupFailure_RemovesNewAttemptAndClosesCurrentConnection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = await Fixture.CreateAsync();
            var displaced = SelectedSessionIdentity.Create(
                fixture.AccountId, new CharacterId(500), new NetworkConnectionId("Game:old-fault"),
                1, StubChannelEntryPolicy.GeneralChannel.ListenerPort);
            fixture.Directory.Select(displaced);
            fixture.Cleanup.FailureSession = displaced;

            await Assert.ThrowsAsync<IOException>(async () =>
                await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None));

            Assert.Null(fixture.Directory.Find(fixture.AccountId));
            Assert.Contains(fixture.ConnectionId, fixture.Closer.Closed);
            Assert.Contains(displaced.ConnectionId, fixture.Closer.Closed);
        });
    }

    private sealed class Fixture
    {
        private Fixture(
            AccountId accountId,
            NetworkConnectionId connectionId,
            SelectCharacterResult result,
            List<string> ledger,
            ISelectedSessionDomainService directory,
            RecordingActive active,
            RecordingCloser closer,
            UserDomainServices users,
            RecordingCleanup cleanup,
            CharacterMutations mutations,
            InventoryPreparation inventory,
            RecordingSender sender,
            RecordingNotifications notifications,
            SessionEntry sessionEntry,
            Preparation? preparation,
            RecordingCommandBus bus,
            SelectCharacterHandler handler,
            SelectCharacterRequest command,
            OnlineUserId onlineUserId,
            long authenticatedGeneration)
        {
            AccountId = accountId;
            ConnectionId = connectionId;
            Result = result;
            Ledger = ledger;
            Directory = directory;
            Active = active;
            Closer = closer;
            Users = users;
            Cleanup = cleanup;
            Mutations = mutations;
            Inventory = inventory;
            Sender = sender;
            Notifications = notifications;
            SessionEntry = sessionEntry;
            Preparation = preparation;
            Bus = bus;
            Handler = handler;
            Command = command;
            OnlineUserId = onlineUserId;
            AuthenticatedGeneration = authenticatedGeneration;
            Context = new NetworkDispatchContext(new NetworkConnectionContext(
                connectionId,
                NetworkEndpointKind.Game,
                StubChannelEntryPolicy.GeneralChannel.ListenerPort));
        }

        public AccountId AccountId { get; }
        public NetworkConnectionId ConnectionId { get; }
        public SelectCharacterResult Result { get; }
        public List<string> Ledger { get; }
        public ISelectedSessionDomainService Directory { get; }
        public RecordingActive Active { get; }
        public RecordingCloser Closer { get; }
        public UserDomainServices Users { get; }
        public RecordingCleanup Cleanup { get; }
        public CharacterMutations Mutations { get; }
        public InventoryPreparation Inventory { get; }
        public RecordingSender Sender { get; }
        public RecordingNotifications Notifications { get; }
        public SessionEntry SessionEntry { get; }
        public Preparation? Preparation { get; }
        public RecordingCommandBus Bus { get; }
        public SelectCharacterHandler Handler { get; }
        public SelectCharacterRequest Command { get; }

        /// <summary>
        /// The connected user this fixture logs in. Which character an account is playing is read
        /// back from this user's own snapshot, which is where publication binds it.
        /// </summary>
        public OnlineUserId OnlineUserId { get; }

        public long AuthenticatedGeneration { get; }
        public NetworkDispatchContext Context { get; }

        public static async ValueTask<Fixture> CreateAsync(
            bool failTrailingLocks = false,
            bool refuseAdmission = false,
            bool planPvp = false,
            bool failPvp = false,
            bool failPvpTarget = false,
            bool failStatic = false,
            bool changeQuest = false,
            bool revivePet = false,
            bool blockStatic = false,
            bool invalidWireIdentity = false,
            bool failPreparation = false,
            bool missingRoster = false,
            bool missingSkillMirror = false,
            bool refillCommits = false,
            bool planTown = false,
            bool failFirstTownPublication = false,
            bool refuseExpiredCleanup = false,
            bool failExpiredCleanup = false,
            ITauKingdomDomainService? tau = null,
            IGameMessageSender? gameSender = null,
            INetworkConnectionCloser? connectionCloser = null,
            bool failQuest = false,
            ICleanupNotificationDelivery? cleanupDelivery = null)
        {
            return await TestGameplay.RunAsync(async () =>
            {
                var accountId = new AccountId(501);
                var connectionId = new NetworkConnectionId("Game:lifecycle");
                var result = await CharacterEntryInventoryTests.CreateLifecycleResultAsync(accountId, 501, 3);
                if (invalidWireIdentity)
                    result = result with
                    {
                        Character = result.Character with
                        {
                            Identity = result.Character.Identity with
                            { CharacterId = new CharacterId(ushort.MaxValue + 1) }
                        }
                    };
                if (missingRoster)
                    result = result with { RosterEntry = null };
                if (missingSkillMirror)
                    result = result with
                    {
                        CharacterEntry = result.CharacterEntry with
                        {
                            Skills = result.CharacterEntry.Skills with { ProtocolMirror = null },
                        },
                    };
                if (planTown)
                    result = result with
                    {
                        Session = result.Session with
                        {
                            Adjacent = result.Session.Adjacent with
                            {
                                TownPresence = new TownPresenceEntryState(
                                    result.World.Channel.Spawn.TownId,
                                    result.World.Channel.Spawn.AreaId,
                                    [new PresentCharacterState(
                                        new CharacterId(4243),
                                        result.World.Channel.Spawn.TownId,
                                        result.World.Channel.Spawn.AreaId)]),
                            },
                        },
                    };
                var ledger = new List<string>();
                var users = new UserDomainServices(accountId);
                var registered = users.RegisterConnection(new NetworkConnectionContext(
                    connectionId, NetworkEndpointKind.Game, StubChannelEntryPolicy.GeneralChannel.ListenerPort));
                await users.AuthenticateForTestAsync(registered.OnlineUserId);
                var authenticated = users.GetRequired(registered.OnlineUserId);
                var directory = users.Sessions;
                var active = new RecordingActive(directory, ledger);
                var closer = new RecordingCloser(ledger);
                var cleanup = new RecordingCleanup(directory, ledger);
                var mutations = new CharacterMutations(ledger);
                var inventory = new InventoryPreparation(result, ledger, refillCommits);
                var sender = new RecordingSender(
                    ledger, failTrailingLocks, failPvp, failPvpTarget, failStatic, blockStatic);
                var sessionEntry = new SessionEntry(ledger, planPvp);
                var notifications = new RecordingNotifications(ledger, failFirstTownPublication);
                var partyProjection = new RecordingPartyTownProjectionDelivery(ledger);
                var townAreaProjection = new TownAreaProjectionDelivery(
                    new SessionPresenceDomainService(),
                    directory,
                    new EmptyCurrentViews(),
                    notifications,
                    partyProjection,
                    gameSender ?? sender,
                    new EveryTownMovement());
                var preparation = refuseAdmission
                    ? null
                    : new Preparation(result, ledger, failPreparation, mutations);
                var prepareHandler = new PrepareSelectedCharacterSessionStageHandler(
                    users,
                    refuseAdmission ? new RefusingPreparation(ledger) : preparation!,
                    directory,
                    cleanup);
                var bus = new RecordingCommandBus(
                    prepareHandler,
                    new CleanupExpiredCharacterItemsStageHandler(
                        new StubExpiredCleanup(refuseExpiredCleanup, failExpiredCleanup)),
                    new PrepareSelectedCharacterStateStageHandler(
                        refuseAdmission ? new RefusingPreparation(ledger) : preparation!, inventory),
                    new ApplySelectedCharacterDailyRefillStageHandler(inventory),
                    new PublishSelectedCharacterSessionStageHandler(active, sessionEntry),
                    new MarkSelectedCharacterPvpReadyStageHandler(sessionEntry),
                    new SynchronizeSelectedCharacterQuestsStageHandler(
                        new QuestEntry(ledger, changeQuest, failQuest)),
                    new ReviveSelectedCharacterPetStageHandler(
                        new PetEntry(ledger, revivePet)),
                    new CompensateSelectedCharacterSessionStageHandler(cleanup, directory));
                bus.Workflow = new LifecycleWorkflow(users, new TestEntryChannel(), new StubStateMessageFactory(), directory,
                    gameSender ?? sender, notifications, connectionCloser ?? closer, bus,
                    cleanupDelivery ?? new NoopCleanupDelivery(), townAreaProjection, tau);
                var handler = new SelectCharacterHandler(bus);
                var command = new SelectCharacterRequest(GameMessageMetadata.Empty, 3);
                return new Fixture(
                    accountId, connectionId, result, ledger, directory, active, closer,
                    users, cleanup, mutations, inventory, sender, notifications, sessionEntry,
                    preparation, bus, handler, command,
                    registered.OnlineUserId, authenticated.Generation);
            });
        }
    }

    private sealed class EveryTownMovement : ITownMovementDomainService
    {
        public ValueTask<bool> DeclaresAreaAsync(
            int townId, int areaId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> SharesTownPresenceAsync(
            int townId, int areaId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> CanEnterTownAsync(
            ChannelEntryIdentity channel, int townId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);
    }

    private sealed class RecordingCommandBus(
        PrepareSelectedCharacterSessionStageHandler prepare,
        CleanupExpiredCharacterItemsStageHandler cleanupExpired,
        PrepareSelectedCharacterStateStageHandler prepareState,
        ApplySelectedCharacterDailyRefillStageHandler refill,
        PublishSelectedCharacterSessionStageHandler publish,
        MarkSelectedCharacterPvpReadyStageHandler markPvpReady,
        SynchronizeSelectedCharacterQuestsStageHandler quests,
        ReviveSelectedCharacterPetStageHandler pet,
        CompensateSelectedCharacterSessionStageHandler compensate) : ICommandBus
    {
        public LifecycleWorkflow? Workflow { get; set; }
        public SelectCharacterResult? State { get; private set; }
        public List<Type> DispatchedCommands { get; } = [];

        public async ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            if (command is not GameSelectCharacterCommand whole) throw new NotSupportedException(command.GetType().Name);
            var ctx = new CommandExecutionContext(Guid.NewGuid(), typeof(GameSelectCharacterCommand), typeof(LifecycleWorkflow), CommandExecutionKind.Direct)
            { Request = new CommandRequestContext.ProtocolNonEffect(whole.Dispatch,whole.Input,new ProtocolFrameMetadata.Game(whole.Input.Metadata),()=>true) };
            return (TResult)(object)await Workflow!.HandleAsync(ctx, whole, cancellationToken);
        }
        public async ValueTask<TResult> SendStageAsync<TResult>(ISelectionStage<TResult> command, CancellationToken cancellationToken=default) where TResult:notnull
        {
            DispatchedCommands.Add(command.GetType());
            object result = command switch
            {
                PrepareSelectedCharacterSessionStage value =>
                    await RunAsync(prepare, value, cancellationToken),
                CleanupExpiredCharacterItemsStage value =>
                    await RunAsync(cleanupExpired, value, cancellationToken),
                EnsureDailyContractPuppetsStage => false,
                InitializeHousingStage => false,
                RefillTauStage => true,
                PrepareSelectedCharacterStateStage value =>
                    await RunAsync(prepareState, value, cancellationToken),
                ApplySelectedCharacterDailyRefillStage value =>
                    await RunAsync(refill, value, cancellationToken),
                PublishSelectedCharacterSessionStage value =>
                    await RunAsync(publish, value, cancellationToken),
                MarkSelectedCharacterPvpReadyStage value =>
                    await RunAsync(markPvpReady, value, cancellationToken),
                SynchronizeSelectedCharacterQuestsStage value =>
                    await RunAsync(quests, value, cancellationToken),
                ReviveSelectedCharacterPetStage value =>
                    await RunAsync(pet, value, cancellationToken),
                CompensateSelectedCharacterSessionStage value =>
                    await RunAsync(compensate, value, cancellationToken),
                _ => throw new InvalidOperationException(
                    $"Unexpected command '{command.GetType().Name}'."),
            };

            switch (result)
            {
                case SelectCharacterResult value:
                    State = value;
                    break;
                case EntryInventoryPreparation value:
                    State = value.Result;
                    break;
                case SynchronizedSelectedCharacterQuests { Change: { } value }:
                    State = value.Result;
                    break;
                case RevivedSelectedCharacterPet { Change: { } value }:
                    State = value.Result;
                    break;
            }

            return (TResult)result;
        }

        private static async ValueTask<TResult> RunAsync<TCommand, TResult>(
            ISelectionStageHandler<TCommand, TResult> handler,
            TCommand command,
            CancellationToken cancellationToken)
            where TCommand : class, ISelectionStage<TResult>
            where TResult : notnull
        {
            using var footprint = new TestEffectFootprints.Lease(Array.Empty<Dfo.Server.Abstractions.Effects.EffectFootprintOwner>());
            var context = new CommandExecutionContext(Guid.NewGuid(), command.GetType(), handler.GetType(), CommandExecutionKind.Workflow);
            context.SupplyLegacyFootprint(footprint);
            return await handler.HandleAsync(context, command, cancellationToken);
        }
    }

    private sealed class TestEntryChannel : Dfo.Server.Abstractions.Channel.IChannelEntryDomainService
    {
        public ValueTask<ChannelEntryIdentity> ResolveAsync(int port, CancellationToken ct) => new StubChannelEntryPolicy().ResolveAsync(port, ct);
        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int level) => new StubChannelEntryPolicy().Refuse(channel, level);
        public ValueTask<Dfo.Server.Data.Channel.ChannelLoginEntry> ResolveLoginAsync(int port, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class LifecycleWorkflow : GameSelectCharacterCommandHandler
    {
        private readonly RecordingCommandBus stages;
        private readonly ICleanupNotificationDelivery cleanupDelivery;
        private readonly TownAreaProjectionDelivery town;
        public LifecycleWorkflow(IOnlineUserQueryDomainService users, Dfo.Server.Abstractions.Channel.IChannelEntryDomainService channels,
            ISelectedCharacterStateMessageFactory factory, ISelectedSessionDomainService sessions, IGameMessageSender sender,
            IEventService events, INetworkConnectionCloser closer, RecordingCommandBus stages,
            ICleanupNotificationDelivery cleanupDelivery, TownAreaProjectionDelivery town, ITauKingdomDomainService? tau)
            : base(users, channels, factory, sessions, new TestGameOutbox(sender), new TestProjections(events), new TestClosures(closer),
                cleanup: null!, units: null!, preparation: null!, inventoryEntry: null!, sessionEntry: null!, questEntry: null!, petEntry: null!,
                expiredItems: null!, puppets: null!, housingEntry: null!, parties: null!, projectionState: new PartyProjectionStreamRegistry(), townAreas: null!,
                logger: NullLogger<GameSelectCharacterCommandHandler>.Instance, delivery: SelectCharacterDeliveryOptions.Everything, clock: TimeProvider.System, tau: tau)
        { this.stages=stages; this.cleanupDelivery=cleanupDelivery; this.town=town; }
        protected override ValueTask<PreparedSelectedCharacterSession> PrepareSessionAsync(CommandExecutionContext x, AccountId account,
            ushort slot, ChannelEntryIdentity channel, NetworkConnectionId id, long generation, CancellationToken ct) =>
            stages.SendStageAsync(new PrepareSelectedCharacterSessionStage(account, slot, channel, id, generation), ct);
        protected internal override async ValueTask<bool> CleanupExpiredAsync(CommandExecutionContext x, SelectedSessionIdentity session, Action<Exception> failed, CancellationToken ct)
        { try { return await stages.SendStageAsync(new CleanupExpiredCharacterItemsStage(session), ct); } catch (Exception error) { failed(error); x.Request!.Failed(error); throw; } }
        protected override ValueTask<bool> EnsurePuppetsAsync(CommandExecutionContext x, SelectedSessionIdentity s, CancellationToken ct) => stages.SendStageAsync(new EnsureDailyContractPuppetsStage(s), ct);
        protected override ValueTask<bool> InitializeHousingAsync(CommandExecutionContext x, SelectedSessionIdentity s, CancellationToken ct) => stages.SendStageAsync(new InitializeHousingStage(s), ct);
        protected internal override ValueTask<SelectCharacterResult> PrepareStateAsync(CommandExecutionContext x, SelectedSessionIdentity s, ActiveCharacterState selected, ChannelEntryIdentity channel, CancellationToken ct) => stages.SendStageAsync(new PrepareSelectedCharacterStateStage(s, selected, channel), ct);
        protected internal override ValueTask<EntryInventoryPreparation> DailyRefillAsync(CommandExecutionContext x, SelectedSessionIdentity s, SelectCharacterResult prepared, CancellationToken ct) => stages.SendStageAsync(new ApplySelectedCharacterDailyRefillStage(s, prepared), ct);
        protected internal override ValueTask<PublishedSelectedCharacterSession> PublishAsync(SelectedSessionIdentity s, SelectCharacterResult prepared, CancellationToken ct) => stages.SendStageAsync(new PublishSelectedCharacterSessionStage(s, prepared), ct);
        protected override ValueTask<bool> RefillTauAsync(CommandExecutionContext x, SelectedSessionIdentity s, CancellationToken ct) => stages.SendStageAsync(new RefillTauStage(s), ct);
        protected internal override ValueTask<SelectedCharacterPvpPublication> MarkPvpReadyAsync(SelectedSessionIdentity s, SelectCharacterResult prepared, CancellationToken ct) => stages.SendStageAsync(new MarkSelectedCharacterPvpReadyStage(s, prepared), ct);
        protected internal override ValueTask<SynchronizedSelectedCharacterQuests> SynchronizeQuestsAsync(CommandExecutionContext x, SelectedSessionIdentity s, SelectCharacterResult prepared, CancellationToken ct) => stages.SendStageAsync(new SynchronizeSelectedCharacterQuestsStage(s, prepared), ct);
        protected internal override ValueTask<RevivedSelectedCharacterPet> RevivePetAsync(CommandExecutionContext x, SelectedSessionIdentity s, SelectCharacterResult prepared, CancellationToken ct) => stages.SendStageAsync(new ReviveSelectedCharacterPetStage(s, prepared), ct);
        protected internal override async ValueTask<SelectedSessionCleanupNotificationPlan> CompensateAsync(CommandExecutionContext x, SelectedSessionIdentity s, CancellationToken ct) => (await stages.SendStageAsync(new CompensateSelectedCharacterSessionStage(s), ct)).Notifications;
        protected override ValueTask DeliverCleanupAsync(SelectedSessionCleanupNotificationPlan plan, CancellationToken ct) => cleanupDelivery.DeliverAsync(plan, ct);
        protected override ValueTask PublishCommittedEntryAsync(SelectedSessionIdentity s, TownParticipantPublicationPlan plan, CancellationToken ct) => town.PublishCommittedEntryAsync(s, plan, ct);
    }
    private sealed class TestGameOutbox(IGameMessageSender sender) : IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken ct) => sender.SendCmdAsync(id,message,ct);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken ct) => sender.SendNotiAsync(id,message,ct);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken ct) => sender.SendExplicitAsync(id,delivery,ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext target, IReadOnlyList<IGameServerMessage> messages, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class TestProjections(IEventService events) : ICommandProjections
    {
        public ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience, T notification, CancellationToken ct=default) where T:class, INotification<T> => events.ProjectAsync(audience,notification,ct);
    }
    private sealed class TestClosures(INetworkConnectionCloser closer) : ICommandConnectionClosures
    {
        public ValueTask ResolveAndRequestCloseAsync(NetworkConnectionId id, CancellationToken ct) => closer.RequestCloseAsync(id,ct);
        public ValueTask RequestCloseAsync(NetworkConnectionContext target, CancellationToken ct) => closer.RequestCloseExactAsync(target,ct);
    }

    private sealed class StubExpiredCleanup(bool refuse, bool fail) : IExpiredCharacterItemCleanupDomainService
    {
        public ValueTask<ExpiredItemCleanupResult> CleanupAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            fail ? ValueTask.FromException<ExpiredItemCleanupResult>(new IOException("database write failed"))
                : ValueTask.FromResult(new ExpiredItemCleanupResult(!refuse, []));
    }

    private sealed class RejectedLoginAuthentication : Dfo.Server.Abstractions.Authentication.IGameLoginAuthenticationDomainService
    {
        public ValueTask<AccountId?> AuthenticateSessionAsync(OnlineUserId id, string mId, string ticket, CancellationToken ct) =>
            ValueTask.FromResult<AccountId?>(null);
    }

    private sealed class UnusedAccountEntryStateDomainService : IAccountEntryStateDomainService
    {
        public ValueTask<AccountEntryState> LoadAsync(
            AccountId accountId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Rejected login must not load account state.");

        public ValueTask<AccountSettingsEntryState> LoadSettingsAsync(
            AccountId accountId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Rejected login must not load account settings.");
    }

    private sealed class StubStateMessageFactory : ISelectedCharacterStateMessageFactory
    {
        public IReadOnlyList<IGameServerMessage> CreateValidated(SelectCharacterResult result) =>
            [new StubStateMessage()];

        private sealed record StubStateMessage : IGameNotiMessage
        {
            public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization;
        }
    }


    private sealed class Preparation(
        SelectCharacterResult result, List<string> ledger, bool failPreparation,
        ISelectedSessionPositionPersistence positions)
        : ISelectCharacterPreparationDomainService
    {
        /// <summary>The real service's own act: stamp the spawn, then store it when the channel keeps positions.</summary>
        public async ValueTask<SelectCharacterResult> PlaceAtSpawnAsync(
            SelectedSessionIdentity session, SelectCharacterResult prepared,
            CancellationToken cancellationToken)
        {
            var spawn = prepared.World.Channel.Spawn;
            var resolved = new CharacterPositionState(
                spawn.TownId, spawn.AreaId, spawn.X, spawn.Y, spawn.Direction);
            var placed = prepared with
            {
                Character = prepared.Character with { Position = resolved },
            };
            if (placed.World.Channel.PersistsPosition)
            {
                await positions.PersistAsync(
                    session.AccountId, session.CharacterId, resolved, cancellationToken)
                    .ConfigureAwait(false);
            }

            return placed;
        }

        public int FullPreparationCalls { get; private set; }
        public ValueTask<ActiveCharacterState> PreflightAsync(
            AccountId accountId, ushort slotIndex, Dfo.Server.Data.Channel.ChannelEntryIdentity channel,
            CancellationToken cancellationToken)
        { ledger.Add("prepare"); return ValueTask.FromResult(result.Character); }
        public ValueTask<SelectCharacterResult> PrepareAsync(
            ActiveCharacterState selected, AccountId accountId,
            Dfo.Server.Data.Channel.ChannelEntryIdentity channel, CancellationToken cancellationToken)
        {
            FullPreparationCalls++;
            return failPreparation
                ? ValueTask.FromException<SelectCharacterResult>(new IOException("full preparation failed"))
                : ValueTask.FromResult(result);
        }
    }

    private sealed class RefusingPreparation(List<string> ledger) : ISelectCharacterPreparationDomainService
    {
        public ValueTask<ActiveCharacterState> PreflightAsync(
            AccountId accountId, ushort slotIndex, Dfo.Server.Data.Channel.ChannelEntryIdentity channel,
            CancellationToken cancellationToken)
        {
            ledger.Add("prepare");
            return ValueTask.FromException<ActiveCharacterState>(
                new Dfo.Server.Data.Channel.ChannelAdmissionRefusedException(
                    channel, new Dfo.Server.Data.Channel.ChannelAdmissionRefusal(9, "restricted")));
        }
        public ValueTask<SelectCharacterResult> PrepareAsync(ActiveCharacterState selected,
            AccountId accountId, Dfo.Server.Data.Channel.ChannelEntryIdentity channel,
            CancellationToken cancellationToken) => throw new InvalidOperationException();

        public ValueTask<SelectCharacterResult> PlaceAtSpawnAsync(
            SelectedSessionIdentity session, SelectCharacterResult prepared,
            CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class InventoryPreparation(
        SelectCharacterResult result, List<string> ledger, bool commitsRefill)
        : ISelectCharacterEntryInventoryDomainService
    {
        /// <summary>
        /// What a refill that actually topped the character up leaves behind.
        /// </summary>
        /// <remarks>
        /// The fixture's own inventory is empty, so a committed change has to be stated here. It
        /// exists so that the frame the handler builds from it -- the bag change the client is told
        /// about -- has something to describe; without it the handler sends nothing and no test can
        /// tell that road from a missing one.
        /// </remarks>
        private static readonly CommittedInventoryUpdateState RefillCommit = new(
            InventoryContainerType.Main,
            [new InventoryItemState(null, 3, StackableItemCore.Create(2, 1001, 1))],
            DateTimeOffset.UnixEpoch);

        public int Claims { get; private set; }
        public int Saves { get; private set; }
        public bool FailSave { get; set; }
        public ValueTask SaveExistingLeaseAsync(SelectedSessionIdentity previousSession,
            CancellationToken cancellationToken)
        {
            if (FailSave) throw new IOException("inventory save failed");
            Saves++;
            return ValueTask.CompletedTask;
        }
        public ValueTask<SelectCharacterResult> PrepareLeaseAsync(
            SelectedSessionIdentity session, SelectCharacterResult current,
            CancellationToken cancellationToken) => ValueTask.FromResult(current);
        public ValueTask<EntryInventoryPreparation> ApplyDailyRefillAsync(
            SelectedSessionIdentity session, SelectCharacterResult current,
            CancellationToken cancellationToken)
        {
            ledger.Add("inventory");
            Claims++;
            return ValueTask.FromResult(new EntryInventoryPreparation(
                current with { CharacterInventory = result.CharacterInventory },
                commitsRefill ? [RefillCommit] : []));
        }
    }

    private sealed class SessionEntry(
        List<string> ledger, bool planPvp)
        : ISelectCharacterSessionEntryDomainService
    {
        public ValueTask<TownParticipantPublicationPlan?> PlanTownPublicationAsync(
            SelectedSessionIdentity session, SelectCharacterResult result,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlanTownPublication(session, result));
        public ValueTask<PvpLobbySynchronizationState?> PlanPvpAsync(
            SelectedSessionIdentity session, SelectCharacterResult result,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlanPvp(session, result));
        public SelectCharacterResult? RuntimeResult { get; private set; }
        public TownParticipantPublicationPlan? PlanTownPublication(
            SelectedSessionIdentity session, SelectCharacterResult result)
        {
            var nearby = result.Session.Adjacent.TownPresence.Others;
            if (nearby.Count == 0)
                return null;

            var spawn = result.World.Channel.Spawn;
            var existing = nearby.Select(other => Participant(
                result with
                {
                    Character = result.Character with
                    {
                        Identity = result.Character.Identity with
                        { CharacterId = other.CharacterId },
                    },
                },
                spawn with { TownId = other.TownId, AreaId = other.AreaId })).ToArray();
            return new TownParticipantPublicationPlan(
                [.. nearby.Select(other => SelectedSessionIdentity.Create(
                    new AccountId(other.CharacterId.Value),
                    other.CharacterId,
                    new NetworkConnectionId($"Game:nearby-{other.CharacterId.Value}"),
                    connectionGeneration: 1,
                    session.ListenerPort))],
                new TownPresenceSynchronizationState(
                    spawn.TownId,
                    spawn.AreaId,
                    Participant(result, spawn),
                    existing));
        }

        public PvpLobbySynchronizationState? PlanPvp(
            SelectedSessionIdentity session, SelectCharacterResult result)
        {
            ledger.Add("plan-pvp");
            return planPvp
                ? new PvpLobbySynchronizationState(
                    [], [], Participant(result))
                : null;
        }

        public PvpParticipantPublicationPlan PlanPvpPublication(
            SelectedSessionIdentity session, SelectCharacterResult result) =>
            new(planPvp ? [new CharacterId(4242)] : [], Participant(result));

        public void Publish(SelectedSessionIdentity session, SelectCharacterResult result)
        {
            RuntimeResult = result;
            ledger.Add("publish");
        }

        public void MarkPvpReady(SelectedSessionIdentity session, SelectCharacterResult result) =>
            ledger.Add("ready");

        private static PvpParticipantEntryState Participant(SelectCharacterResult result)
        {
            var equipment = result.CharacterInventory.Containers
                .SingleOrDefault(value => value.ContainerType == InventoryContainerType.Equipment)
                ?.Items ?? [];
            return new(
                result.Character,
                result.CharacterEntry.Public,
                equipment,
                result.CharacterEntry.Features.Pets);
        }

        private static TownParticipantEntryState Participant(
            SelectCharacterResult result,
            EntrySpawnState spawn)
        {
            var equipment = result.CharacterInventory.Containers
                .SingleOrDefault(value => value.ContainerType == InventoryContainerType.Equipment)
                ?.Items ?? [];
            return new TownParticipantEntryState(
                result.Character,
                result.CharacterEntry.Public,
                equipment,
                result.CharacterEntry.Features.Pets,
                spawn);
        }
    }

    private sealed class QuestEntry(List<string> ledger, bool changes, bool fails = false) : ISelectCharacterQuestEntryDomainService
    {
        public ValueTask<SelectCharacterQuestSynchronization?> SynchronizeAsync(
            SelectedSessionIdentity session, SelectCharacterResult result,
            CancellationToken cancellationToken)
        {
            ledger.Add("quest");
            if (fails)
                return ValueTask.FromException<SelectCharacterQuestSynchronization?>(
                    new IOException("quest synchronization failed"));
            if (changes)
            {
                var updated = result with
                {
                    CharacterEntry = result.CharacterEntry with
                    {
                        Quests = result.CharacterEntry.Quests with
                        { ActiveQuests = [new CharacterQuestProgressState(0, 77, 3)] }
                    }
                };
                return ValueTask.FromResult<SelectCharacterQuestSynchronization?>(new(
                    updated, new AcceptedQuestSynchronizationState(
                        [new AcceptedQuestProgressState(77, 3)])));
            }
            return ValueTask.FromResult<SelectCharacterQuestSynchronization?>(null);
        }
    }

    private sealed class PetEntry(List<string> ledger, bool changes) : ISelectCharacterPetEntryDomainService
    {
        public ValueTask<SelectCharacterPetRevival?> ReviveAsync(
            SelectedSessionIdentity session, SelectCharacterResult result,
            CancellationToken cancellationToken)
        {
            ledger.Add("pet");
            if (changes)
            {
                var updated = result with
                {
                    CharacterEntry = result.CharacterEntry with
                    {
                        Features = result.CharacterEntry.Features with
                        { Pets = [new CharacterPetRuntimeState(88, 1, 0, 0, 0, 0, 0, [], 0)] }
                    }
                };
                return ValueTask.FromResult<SelectCharacterPetRevival?>(new(
                    updated, new PetRevivalState(session.WireUserId, 88, 1)));
            }
            return ValueTask.FromResult<SelectCharacterPetRevival?>(null);
        }
    }

    private sealed class CharacterMutations(List<string> ledger) : ISelectedSessionPositionPersistence
    {
        public CharacterPositionState? PersistedPosition { get; private set; }

        public ValueTask PersistAsync(
            AccountId accountId, CharacterId characterId, CharacterPositionState position,
            CancellationToken cancellationToken)
        {
            ledger.Add("position");
            PersistedPosition = position;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>The selected-session directory, recording the moment a session enters the world.</summary>
    private sealed class RecordingActive(ISelectedSessionDomainService inner, List<string> ledger)
        : ISelectedSessionDomainService
    {
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => inner.Select(session);

        public void EnterWorld(SelectedSessionIdentity session)
        {
            ledger.Add("active");
            inner.EnterWorld(session);
        }

        public bool Fence(SelectedSessionIdentity session) => inner.Fence(session);
        public bool ReleaseExact(SelectedSessionIdentity session) => inner.ReleaseExact(session);
        public SelectedSessionIdentity? Find(AccountId accountId) => inner.Find(accountId);
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => inner.FindByCharacter(characterId);
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => inner.FindByWireUserId(wireUserId);
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => inner.FindByConnection(connectionId);
        public SelectedSessionStanding? StandingOf(SelectedSessionIdentity session) => inner.StandingOf(session);
        public bool IsInWorld(SelectedSessionIdentity session) => inner.IsInWorld(session);
        public bool IsSelected(SelectedSessionIdentity session) => inner.IsSelected(session);
        public bool TryGetInWorld(CharacterId characterId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
            inner.TryGetInWorld(characterId, out session);
        public bool TryGetInWorld(ushort wireUserId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
            inner.TryGetInWorld(wireUserId, out session);
        public bool TryGetInWorld(NetworkConnectionId connectionId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
            inner.TryGetInWorld(connectionId, out session);
    }

    private sealed class RecordingCleanup(
        ISelectedSessionDomainService directory, List<string> ledger)
        : ISelectedSessionCleanupDomainService
    {
        public List<(SelectedSessionIdentity Session, bool IncludeOwner)> Calls { get; } = [];
        public List<SelectedSessionIdentity?> SelectedOwnersAtCall { get; } = [];
        public SelectedSessionIdentity? FailureSession { get; set; }

        /// <summary>The plan the cleanup hands back; the empty one unless a case needs people around the session.</summary>
        public Func<SelectedSessionIdentity, SelectedSessionCleanupNotificationPlan>? Plan { get; set; }

        public ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
            SelectedSessionIdentity session, bool includeOwner, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken cancellationToken)
        {
            ledger.Add("cleanup");
            Calls.Add((session, includeOwner));
            SelectedOwnersAtCall.Add(directory.Find(session.AccountId));
            if (FailureSession == session)
                return ValueTask.FromException<SelectedSessionCleanupNotificationPlan>(
                    new IOException("cleanup failed"));
            _ = directory.ReleaseExact(session);
            return ValueTask.FromResult(Plan?.Invoke(session) ?? SelectedSessionCleanupNotificationPlan.Empty(session));
        }
    }

    /// <summary>
    /// A real registration adapter under the workflow (TASK-25): every call goes to the real protocol sender over it,
    /// and the ledger word for a send is written at its registration point, before it is encoded, as the old double
    /// wrote it at the call. The failure switches make the chosen direct messages fail to encode where they are
    /// registered, with the exceptions the old double threw; <c>blockStatic</c> holds the first static message at its
    /// registration point until released.
    /// </summary>
    private sealed class RecordingSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();
        private readonly List<string> ledger;
        private readonly bool blockStatic;
        private bool failingStatic;

        public RecordingSender(
            List<string> ledger, bool failTrailingLocks, bool failPvp,
            bool failPvpTarget, bool failStatic, bool blockStatic)
        {
            this.ledger = ledger;
            this.blockStatic = blockStatic;
            FailStatic = failStatic;
            outbound.FailAny(_ => failingStatic, () => new IOException("static delivery failed"));
            if (failPvp)
                outbound.FailAny(message => FirstDirect(message) == "PvpLobbySnapshotMessage",
                    () => new IOException("PvP direct delivery failed"));
            if (failTrailingLocks)
                outbound.FailAny(message => FirstDirect(message) == "TrailingEquipmentLockSnapshotMessage",
                    () => new IOException("required delivery failed"));
            // The Networking sender owns target isolation; a target transport failure is
            // represented here by a completed best-effort batch.
            _ = failPvpTarget;
        }

        public bool FailStatic { get; set; }
        private readonly TaskCompletionSource staticRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StaticEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseStatic() => staticRelease.TrySetResult();

        /// <summary>State messages that reached the registration point.</summary>
        public int StateMessageCount { get; private set; }

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId, IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            SendClassifiedAsync(message, () => outbound.Game.SendCmdAsync(connectionId, message, cancellationToken));

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            SendClassifiedAsync(message, () => outbound.Game.SendNotiAsync(connectionId, message, cancellationToken));

        private async ValueTask SendClassifiedAsync(IGameServerMessage message, Func<ValueTask> register)
        {
            var firstStatic = false;
            if (message.MessageType == ServerMessageType.GameStateSynchronization)
            {
                StateMessageCount++;
                if (StateMessageCount == 1)
                {
                    firstStatic = true;
                    ledger.Add("static");
                    if (blockStatic)
                    {
                        StaticEntered.TrySetResult();
                        await staticRelease.Task;
                    }
                }
            }
            else ledger.Add("message");
            failingStatic = firstStatic && FailStatic;
            try
            {
                await register();
            }
            finally
            {
                failingStatic = false;
            }
        }

        public ValueTask SendExplicitAsync(
            NetworkConnectionId connectionId, GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            if (delivery.RequiredDirectMessages.Count == 0
                && delivery.BestEffortTargets.Count == 0)
                return outbound.Game.SendExplicitAsync(connectionId, delivery, cancellationToken);
            var messageName = delivery.RequiredDirectMessages.FirstOrDefault()?.GetType().Name;
            var premium = messageName == "PremiumServiceStateMessage";
            var trailing = messageName == "TrailingEquipmentLockSnapshotMessage";
            // The bag change the refill produced. Named apart from the rest because it is the one
            // frame two protocols now share, and "it still goes out here" is what says the lift did
            // not quietly drop the entry path's use of it.
            var delta = messageName == "InventoryDeltaMessage";
            var pvp = messageName == "PvpLobbySnapshotMessage";
            var rejection = messageName == "EntryAdmissionRejectedCommandMessage";
            var target = delivery.BestEffortTargets.Count > 0;
            ledger.Add(rejection ? "x08"
                : pvp ? "pvp"
                : target ? "target"
                : trailing ? "x11"
                : premium ? "premium"
                : delta ? "delta"
                : "pre");
            return outbound.Game.SendExplicitAsync(connectionId, delivery, cancellationToken);
        }

        /// <summary>The type name of the first direct message of the explicit delivery <paramref name="message"/> is a direct message of.</summary>
        private string? FirstDirect(IServerNetworkMessage message) =>
            outbound.ExplicitInFlight is { } delivery && delivery.RequiredDirectMessages.Any(direct => ReferenceEquals(direct, message))
                ? delivery.RequiredDirectMessages[0].GetType().Name
                : null;
    }

    /// <summary>Stands in for the notification service, and keeps the fan-out in the ledger.</summary>
    /// <remarks>
    /// The PvP publication used to leave through the sender, which is why the ledger has a
    /// "target" in it. It leaves through here now, and it records the same word at the same point
    /// -- so what these cases assert about the order of the entry sequence keeps meaning what it
    /// meant. What became of each recipient is not this file's subject; the notification cases
    /// cover that.
    /// </remarks>
    private sealed class RecordingNotifications(
        List<string> ledger,
        bool failFirstTownPublication) : Dfo.Server.Testing.ProjectionEventService
    {
        private int townPublicationAttempts;

        public List<(NotificationAudience Audience, object Notification)> Published { get; } = [];

        public override ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
        {
            ledger.Add("target");
            Published.Add((audience, notification));
            var attempts = audience is NotificationAudience.TheseSessions exact
                ? exact.Sessions.Select(session =>
                    new NotificationAttempt(
                        session.CharacterId,
                        failFirstTownPublication
                            && notification is TownParticipantPublished
                            && Interlocked.Increment(ref townPublicationAttempts) == 1
                            ? NotificationOutcome.Failed
                            : NotificationOutcome.Registered))
                    .ToArray()
                : [];
            return ValueTask.FromResult(new NotificationDelivery(attempts));
        }
    }

    /// <summary>The cleanup plans these cases produce are empty, and this keeps them silent.</summary>
    /// <remarks>
    /// Before the peer half moved to the notification service, an empty plan reached
    /// <see cref="RecordingSender"/> and was dropped there without touching the ledger. This does
    /// the same for the same reason, so the ordered sequences these cases assert keep their shape.
    /// </remarks>
    private sealed class NoopCleanupDelivery : ICleanupNotificationDelivery
    {
        public ValueTask DeliverAsync(
            SelectedSessionCleanupNotificationPlan plan, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingPartyTownProjectionDelivery(List<string> ledger)
        : IPartyTownProjectionDelivery
    {
        public ValueTask RefreshMovedPartyAsync(
            SelectedSessionIdentity mover,
            Dfo.Server.Data.Parties.PartyDirectoryScope? previousScope,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RefreshObservedPartiesAsync(
            IReadOnlyList<SelectedSessionIdentity> recipients,
            CancellationToken cancellationToken)
        {
            ledger.Add(
                $"party:{string.Join(',', recipients.Select(value => value.CharacterId.Value))}");
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// A real closer (TASK-25): the close is registered in the job's outbox and read back from it; the ledger word and
    /// the callback come right after, at the registration point.
    /// </summary>
    private sealed class RecordingCloser(List<string> ledger) : INetworkConnectionCloser
    {
        private readonly RecordedOutbound outbound = new();
        public List<NetworkConnectionId> Closed => [.. outbound.Closes];
        public Func<ValueTask>? OnClose { get; set; }

        public async ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken ct)
        {
            await outbound.Closer.RequestCloseExactAsync(expected, ct);
            await ClosedAsync();
        }

        public async ValueTask RequestCloseAsync(
            NetworkConnectionId connectionId, CancellationToken cancellationToken)
        {
            await outbound.Closer.RequestCloseAsync(connectionId, cancellationToken);
            await ClosedAsync();
        }

        private ValueTask ClosedAsync()
        {
            ledger.Add("close");
            return OnClose?.Invoke() ?? ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyCurrentViews : ISelectedCharacterCurrentViewDomainService
    {
        public ValueTask<SelectCharacterResult?> TryLoadAsync(
            SelectedSessionIdentity session, CancellationToken cancellationToken) =>
            ValueTask.FromResult<SelectCharacterResult?>(null);
    }
}
