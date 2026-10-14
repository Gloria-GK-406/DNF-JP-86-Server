using Dfo.Server.Abstractions.DomainEvents;
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
using Dfo.Server.Application.OnlineUsers;
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

namespace Dfo.Server.Application.Tests.Game;

public sealed partial class SelectCharacterLifecycleTests
{
    [Fact]
    public async Task Achievement_projection_waits_for_the_complete_entry_workflow()
    {
        var gate=new Dfo.Server.Protocol.Game.TitleBook.AchievementTitleProjectionGate();
        var fixture=await Fixture.CreateAsync(blockStatic:true,achievementGate:gate);
        var entering=fixture.Handler.HandleAsync(fixture.Context,fixture.Command,default).AsTask();
        await fixture.Sender.StaticEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var projection=gate.EnterAsync(fixture.Result.Character.Identity.CharacterId,default).AsTask();
        try { Assert.False(projection.IsCompleted); }
        finally { fixture.Sender.ReleaseStatic(); }
        await entering;
        using var lease=await projection.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(fixture.Ledger.IndexOf("publish")>=0);
    }
    [Fact]
    public async Task Success_OrdersRequiredDeliveryBeforePublicationAndPostEntryStages()
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
                typeof(PrepareSelectedCharacterSessionCommand),
                typeof(CleanupExpiredCharacterItemsCommand),
                typeof(EnsureDailyContractPuppetsCommand),
                typeof(PrepareSelectedCharacterStateCommand),
                typeof(ApplySelectedCharacterDailyRefillCommand),
                typeof(PublishSelectedCharacterSessionCommand),
                typeof(SynchronizeSelectedCharacterQuestsCommand),
                typeof(ReviveSelectedCharacterPetCommand),
            ],
            fixture.Bus.DispatchedCommands);
        Assert.NotNull(fixture.Directory.Find(fixture.AccountId));
        Assert.Equal(
            result.Character.Identity.CharacterId,
            fixture.Users.GetRequired(fixture.OnlineUserId).CurrentCharacterId);
        Assert.Empty(fixture.Closer.Closed);
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
        var fixture = await Fixture.CreateAsync(refillCommits: true);

        await fixture.Handler.HandleAsync(
            fixture.Context, fixture.Command, CancellationToken.None);

        Assert.Equal(
            ["prepare", "position", "static", "inventory", "delta", "pre", "active", "publish",
                "plan-pvp", "x11", "quest", "pet", "premium"],
            fixture.Ledger);
    }

    [Fact]
    public async Task RequiredOwnerFailure_CompensatesExactAttemptAndClosesOnlyItsConnection()
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
    }

    [Fact]
    public async Task StaticFailure_DoesNotInvokeDailyRefillMutation()
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
    }

    [Fact]
    public async Task ExpiredItemCleanupRefusal_RejectsBeforeCharacterSnapshotAndCompensates()
    {
        var fixture = await Fixture.CreateAsync(refuseExpiredCleanup: true);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None));

        Assert.Equal(["prepare", "x08", "cleanup", "close"], fixture.Ledger);
        Assert.Equal(0, fixture.Sender.StateMessageCount);
        Assert.Equal(0, fixture.Inventory.Claims);
        Assert.Null(fixture.Directory.Find(fixture.AccountId));
        Assert.DoesNotContain(typeof(PrepareSelectedCharacterStateCommand), fixture.Bus.DispatchedCommands);
    }

    [Fact]
    public async Task ExpiredItemCleanupException_RejectsBeforeCharacterSnapshotAndCompensates()
    {
        var fixture = await Fixture.CreateAsync(failExpiredCleanup: true);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None));

        Assert.IsType<IOException>(error.InnerException);
        Assert.Equal(["prepare", "x08", "cleanup", "close"], fixture.Ledger);
        Assert.Equal(0, fixture.Sender.StateMessageCount);
        Assert.Null(fixture.Directory.Find(fixture.AccountId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingRequiredPrePublicationState_EmitsNoAcceptedOutput(
        bool missingRoster)
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
    }

    [Fact]
    public async Task QuestAndPetChangesRemainInDeliveryWithoutReplacingASessionSnapshot()
    {
        var fixture = await Fixture.CreateAsync(changeQuest: true, revivePet: true);

        await fixture.Handler.HandleAsync(
            fixture.Context, fixture.Command, CancellationToken.None);
        var result = fixture.Bus.State!;

        Assert.Equal(3, Assert.Single(result.CharacterEntry.Quests.ActiveQuests).TriggerValue);
        Assert.Equal(1, Assert.Single(result.CharacterEntry.Features.Pets).Field04);
        Assert.NotNull(fixture.SessionEntry.RuntimeResult);
        Assert.DoesNotContain("runtime-update", fixture.Ledger);
    }

    [Fact]
    public async Task DisconnectWinningTransitionGate_PreventsPreparationMutationAndPublication()
    {
        var fixture = await Fixture.CreateAsync();
        var blocker = await fixture.TransitionGate.AcquireAsync(
            fixture.AccountId, UserAccessMode.Exclusive, CancellationToken.None);
        var observer = new OnlineUserConnectionObserver(
            fixture.Users, new OnlineUserExecutionGate(), fixture.TransitionGate,
            fixture.Directory, fixture.Cleanup, new StubChannelEntryPolicy(),
            new StubServerConfigService(), fixture.Sender, new NoopCleanupDelivery(), TimeProvider.System);
        var connection = new NetworkConnectionContext(
            fixture.ConnectionId, NetworkEndpointKind.Game,
            StubChannelEntryPolicy.GeneralChannel.ListenerPort);
        var disconnect = observer.DisconnectedAsync(connection, CancellationToken.None).AsTask();
        await Task.Yield();
        var command = fixture.Handler.HandleAsync(
            fixture.Context, fixture.Command, CancellationToken.None).AsTask();
        await blocker.DisposeAsync();

        await disconnect;
        await Assert.ThrowsAsync<InvalidOperationException>(() => command);
        Assert.Empty(fixture.Ledger);
        Assert.Null(fixture.Directory.Find(fixture.AccountId));
    }

    [Fact]
    public async Task CommandHoldingTransitionGate_BlocksDisconnectCleanupUntilRegisteredFlowCompletes()
    {
        var fixture = await Fixture.CreateAsync(blockStatic: true);
        var command = fixture.Handler.HandleAsync(
            fixture.Context, fixture.Command, CancellationToken.None).AsTask();
        await fixture.Sender.StaticEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(fixture.Directory.Find(fixture.AccountId));
        var observer = new OnlineUserConnectionObserver(
            fixture.Users, new OnlineUserExecutionGate(), fixture.TransitionGate,
            fixture.Directory, fixture.Cleanup, new StubChannelEntryPolicy(),
            new StubServerConfigService(), fixture.Sender, new NoopCleanupDelivery(), TimeProvider.System);
        var disconnect = observer.DisconnectedAsync(
            new NetworkConnectionContext(fixture.ConnectionId, NetworkEndpointKind.Game,
                StubChannelEntryPolicy.GeneralChannel.ListenerPort), CancellationToken.None).AsTask();
        await Task.Yield();

        Assert.False(disconnect.IsCompleted);
        Assert.Empty(fixture.Cleanup.Calls);
        fixture.Sender.ReleaseStatic();
        await command;
        await disconnect;

        Assert.Single(fixture.Cleanup.Calls);
        Assert.Null(fixture.Directory.Find(fixture.AccountId));
    }

    [Fact]
    public async Task ObserverQueuedBeforeRollbackClose_CompletesAfterGateRelease()
    {
        var fixture = await Fixture.CreateAsync(blockStatic: true, failStatic: true);
        var command = fixture.Handler.HandleAsync(
            fixture.Context, fixture.Command, CancellationToken.None).AsTask();
        await fixture.Sender.StaticEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var observer = new OnlineUserConnectionObserver(
            fixture.Users, new OnlineUserExecutionGate(), fixture.TransitionGate,
            fixture.Directory, fixture.Cleanup, new StubChannelEntryPolicy(),
            new StubServerConfigService(), fixture.Sender, new NoopCleanupDelivery(), TimeProvider.System);
        var disconnect = observer.DisconnectedAsync(
            new NetworkConnectionContext(fixture.ConnectionId, NetworkEndpointKind.Game,
                StubChannelEntryPolicy.GeneralChannel.ListenerPort), CancellationToken.None).AsTask();
        await Task.Yield();
        Assert.False(disconnect.IsCompleted);
        fixture.Closer.OnClose = () => new ValueTask(disconnect);

        fixture.Sender.ReleaseStatic();
        await Assert.ThrowsAsync<IOException>(() => command.WaitAsync(TimeSpan.FromSeconds(2)));
        await disconnect.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains(fixture.ConnectionId, fixture.Closer.Closed);
    }

    [Fact]
    public async Task RejectedRepeatedLoginStillLetsDisconnectObserverCleanupTheSelectedSession()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Handler.HandleAsync(
            fixture.Context, fixture.Command, CancellationToken.None);
        var selected = Assert.IsType<SelectedSessionIdentity>(
            fixture.Directory.FindByConnection(fixture.ConnectionId));
        var sendsBeforeRejection = fixture.Sender.StateMessageCount;
        var login = new GameLoginHandler(
            fixture.Users,
            fixture.Sender,
            new RejectedLoginCommandBus(),
            new StubServerConfigService(),
            new UnusedAccountEntryStateDomainService(),
            fixture.Closer,
            NullLogger<GameLoginHandler>.Instance);

        await login.HandleAsync(
            fixture.Context,
            new GameLoginRequest(
                GameMessageMetadata.Empty,
                "lifecycle",
                "already-consumed-ticket-cannot-be-replayed-0"),
            CancellationToken.None);

        Assert.False(fixture.Users.TryGetByConnection(fixture.ConnectionId, out _));
        Assert.Equal(selected, fixture.Directory.FindByConnection(fixture.ConnectionId));
        Assert.Equal(sendsBeforeRejection, fixture.Sender.StateMessageCount);
        Assert.Contains(fixture.ConnectionId, fixture.Closer.Closed);

        var observer = new OnlineUserConnectionObserver(
            fixture.Users,
            new OnlineUserExecutionGate(),
            fixture.TransitionGate,
            fixture.Directory,
            fixture.Cleanup,
            new StubChannelEntryPolicy(),
            new StubServerConfigService(),
            fixture.Sender,
            new NoopCleanupDelivery(),
            TimeProvider.System);
        await observer.DisconnectedAsync(
            fixture.Context.Connection,
            CancellationToken.None);

        var cleanup = Assert.Single(fixture.Cleanup.Calls);
        Assert.Equal(selected, cleanup.Session);
        Assert.False(cleanup.IncludeOwner);
        Assert.Null(fixture.Directory.FindByConnection(fixture.ConnectionId));
        Assert.Null(fixture.Directory.Find(fixture.AccountId));
    }

    [Fact]
    public async Task OrphanedDisconnectWaitingForLifecycleGateDoesNotCleanupAReplacementSession()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Handler.HandleAsync(
            fixture.Context, fixture.Command, CancellationToken.None);
        var original = Assert.IsType<SelectedSessionIdentity>(
            fixture.Directory.FindByConnection(fixture.ConnectionId));
        var login = new GameLoginHandler(
            fixture.Users,
            fixture.Sender,
            new RejectedLoginCommandBus(),
            new StubServerConfigService(),
            new UnusedAccountEntryStateDomainService(),
            fixture.Closer,
            NullLogger<GameLoginHandler>.Instance);
        await login.HandleAsync(
            fixture.Context,
            new GameLoginRequest(
                GameMessageMetadata.Empty,
                "lifecycle",
                "already-consumed-ticket-cannot-be-replayed-0"),
            CancellationToken.None);

        var observer = new OnlineUserConnectionObserver(
            fixture.Users,
            new OnlineUserExecutionGate(),
            fixture.TransitionGate,
            fixture.Directory,
            fixture.Cleanup,
            new StubChannelEntryPolicy(),
            new StubServerConfigService(),
            fixture.Sender,
            new NoopCleanupDelivery(),
            TimeProvider.System);
        var blocker = await fixture.TransitionGate.AcquireAsync(
            fixture.AccountId, UserAccessMode.Exclusive, CancellationToken.None);
        var disconnect = observer.DisconnectedAsync(
            fixture.Context.Connection, CancellationToken.None).AsTask();
        await Task.Yield();
        Assert.False(disconnect.IsCompleted);

        var replacement = SelectedSessionIdentity.Create(
            fixture.AccountId,
            new CharacterId(650),
            new NetworkConnectionId("Game:lifecycle-replacement"),
            connectionGeneration: 1,
            StubChannelEntryPolicy.GeneralChannel.ListenerPort);
        Assert.Equal(original, fixture.Directory.Select(replacement));
        await blocker.DisposeAsync();
        await disconnect;

        Assert.Empty(fixture.Cleanup.Calls);
        Assert.Equal(replacement, fixture.Directory.Find(fixture.AccountId));
        Assert.Equal(replacement, fixture.Directory.FindByCharacter(replacement.CharacterId));
        Assert.Equal(replacement, fixture.Directory.FindByWireUserId(replacement.WireUserId));
        Assert.Equal(replacement, fixture.Directory.FindByConnection(replacement.ConnectionId));
        Assert.Null(fixture.Directory.FindByConnection(original.ConnectionId));
    }

    [Fact]
    public async Task AdmissionRefusal_SendsOrderedX08AndHasZeroLifecycleSideEffects()
    {
        var fixture = await Fixture.CreateAsync(refuseAdmission: true);

        await Assert.ThrowsAsync<Dfo.Server.Data.Channel.ChannelAdmissionRefusedException>(async () =>
            await fixture.Handler.HandleAsync(
                fixture.Context, fixture.Command, CancellationToken.None));

        Assert.Equal(["prepare", "x08"], fixture.Ledger);
        Assert.Null(fixture.Directory.Find(fixture.AccountId));
        Assert.Null(fixture.Users.GetRequired(fixture.OnlineUserId).CurrentCharacterId);
        Assert.Empty(fixture.Closer.Closed);
    }

    [Fact]
    public async Task AdmissionRefusal_DoesNotSaveOrReleaseExistingDirtyLease()
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
    }

    [Fact]
    public async Task UnrepresentableWireIdentity_HasZeroRegisteredOrDurablePreparationSideEffects()
    {
        var fixture = await Fixture.CreateAsync(invalidWireIdentity: true);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None));

        Assert.Equal(0, fixture.Preparation!.FullPreparationCalls);
        Assert.Equal(["prepare"], fixture.Ledger);
        Assert.Null(fixture.Directory.Find(fixture.AccountId));
        Assert.Empty(fixture.Cleanup.Calls);
        Assert.Empty(fixture.Closer.Closed);
    }

    [Fact]
    public async Task FullPreparationFailure_AfterRegistrationCompensatesExactSessionAndCloses()
    {
        var fixture = await Fixture.CreateAsync(failPreparation: true);

        await Assert.ThrowsAsync<IOException>(async () =>
            await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None));

        Assert.Equal(1, fixture.Preparation!.FullPreparationCalls);
        Assert.Equal(["prepare", "cleanup", "close"], fixture.Ledger);
        Assert.Null(fixture.Directory.Find(fixture.AccountId));
        Assert.Equal([fixture.ConnectionId], fixture.Closer.Closed);
    }

    [Fact]
    public async Task PvpDirectFailure_DoesNotMarkReadyAndRollsBackPublishedAttempt()
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
    }

    [Fact]
    public async Task PvpPeerFailure_IsolatedAfterReadyAndDoesNotPreventTrailingLocks()
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
    }

    [Fact]
    public async Task EnteringAnOccupiedTown_PublishesTheNewcomerToTheExistingCharacters()
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
        Assert.Equal(["party:501<-4243", "party:4243<-501"], partySteps);
        var firstParty = fixture.Ledger.IndexOf(partySteps[0]);
        Assert.Equal("pre", fixture.Ledger[firstParty - 1]);
        Assert.Equal("target", fixture.Ledger[firstParty + 1]);
        Assert.Equal(partySteps[1], fixture.Ledger[firstParty + 2]);
    }

    [Fact]
    public async Task TransientTownPublicationFailureRetriesBeforeSendingPartyState()
    {
        var fixture = await Fixture.CreateAsync(
            planTown: true,
            failFirstTownPublication: true);

        await fixture.Handler.HandleAsync(
            fixture.Context, fixture.Command, CancellationToken.None);

        Assert.Equal(
            ["party:501<-4243", "party:4243<-501"],
            fixture.Ledger
                .Where(value => value.StartsWith("party:", StringComparison.Ordinal)));
        Assert.Equal(2, fixture.Notifications.Published.Count);
    }

    [Fact]
    public async Task SameConnectionSwitch_RefreshesGenerationAfterNormalCleanup()
    {
        var fixture = await Fixture.CreateAsync();
        Assert.True(fixture.Users.TryGetByConnection(fixture.ConnectionId, out var before));
        var old = SelectedSessionIdentity.Create(
            fixture.AccountId, new CharacterId(500), fixture.ConnectionId,
            before!.Generation, StubChannelEntryPolicy.GeneralChannel.ListenerPort);
        fixture.Directory.Select(old);
        fixture.Active.Publish(old);
        fixture.Ledger.Clear();
        Assert.True(fixture.Users.TryGetByConnection(fixture.ConnectionId, out var selected));

        await fixture.Handler.HandleAsync(
            fixture.Context, fixture.Command, CancellationToken.None);

        var current = fixture.Directory.Find(fixture.AccountId)!;
        Assert.True(current.ConnectionGeneration > selected!.Generation);
        Assert.Equal([(old, true)], fixture.Cleanup.Calls);
        Assert.DoesNotContain(fixture.ConnectionId, fixture.Closer.Closed);
        Assert.False(fixture.Directory.ReleaseExact(old));
    }

    [Fact]
    public async Task CrossConnectionDisplacement_CleansCurrentOwnerBeforeRegisteringReplacement()
    {
        var fixture = await Fixture.CreateAsync();
        var displaced = SelectedSessionIdentity.Create(
            fixture.AccountId, new CharacterId(500), new NetworkConnectionId("Game:old"),
            1, StubChannelEntryPolicy.GeneralChannel.ListenerPort);
        fixture.Directory.Select(displaced);
        fixture.Closer.OnClose = async () =>
        {
            await using var callbackLease = await fixture.TransitionGate.AcquireAsync(
                fixture.AccountId, UserAccessMode.Exclusive, CancellationToken.None);
        };

        await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([(displaced, false)], fixture.Cleanup.Calls);
        Assert.Equal([displaced], fixture.Cleanup.SelectedOwnersAtCall);
        Assert.Contains(displaced.ConnectionId, fixture.Closer.Closed);
        Assert.Equal(fixture.ConnectionId, fixture.Directory.Find(fixture.AccountId)!.ConnectionId);
    }

    [Fact]
    public async Task DisplacedCleanupFailure_RemovesNewAttemptAndClosesCurrentConnection()
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
            SelectedSessionTransitionGate transitionGate,
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
            TransitionGate = transitionGate;
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
        public SelectedSessionTransitionGate TransitionGate { get; }
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
            Dfo.Server.Protocol.Game.TitleBook.AchievementTitleProjectionGate? achievementGate = null)
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
            var directory = new SelectedSessionDomainService();
            var active = new RecordingActive(users, ledger);
            var closer = new RecordingCloser(ledger);
            var cleanup = new RecordingCleanup(directory, active, ledger);
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
                sender,
                new EveryTownMovement());
            var transitionGate = new SelectedSessionTransitionGate();
            var preparation = refuseAdmission
                ? null
                : new Preparation(result, ledger, failPreparation, mutations);
            var prepareHandler = new PrepareSelectedCharacterSessionCommandHandler(
                users,
                refuseAdmission ? new RefusingPreparation(ledger) : preparation!,
                directory,
                cleanup);
            var bus = new RecordingCommandBus(
                prepareHandler,
                new CleanupExpiredCharacterItemsCommandHandler(
                    new StubExpiredCleanup(refuseExpiredCleanup, failExpiredCleanup)),
                new PrepareSelectedCharacterStateCommandHandler(
                    refuseAdmission ? new RefusingPreparation(ledger) : preparation!, inventory),
                new ApplySelectedCharacterDailyRefillCommandHandler(inventory),
                new PublishSelectedCharacterSessionCommandHandler(active, sessionEntry),
                new MarkSelectedCharacterPvpReadyCommandHandler(sessionEntry),
                new SynchronizeSelectedCharacterQuestsCommandHandler(
                    new QuestEntry(ledger, changeQuest)),
                new ReviveSelectedCharacterPetCommandHandler(
                    new PetEntry(ledger, revivePet)),
                new CompensateSelectedCharacterSessionCommandHandler(cleanup, directory));
            var handler = new SelectCharacterHandler(
                users,
                new StubChannelEntryPolicy(),
                bus,
                new StubStateMessageFactory(),
                transitionGate,
                directory,
                sender,
                notifications,
                townAreaProjection,
                new NoopCleanupDelivery(),
                closer,
                NullLogger<SelectCharacterHandler>.Instance,
                // These tests describe the full delivery contract, including everything that
                // follows the entry batch. Production runs the reference sequence instead.
                SelectCharacterDeliveryOptions.Everything, achievementProjectionGate:achievementGate);
            var command = new SelectCharacterRequest(GameMessageMetadata.Empty, 3);
            return new Fixture(
                accountId, connectionId, result, ledger, directory, active, closer,
                users, cleanup, mutations, inventory, sender, notifications, sessionEntry,
                preparation, transitionGate, bus, handler, command,
                registered.OnlineUserId, authenticated.Generation);
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
        PrepareSelectedCharacterSessionCommandHandler prepare,
        CleanupExpiredCharacterItemsCommandHandler cleanupExpired,
        PrepareSelectedCharacterStateCommandHandler prepareState,
        ApplySelectedCharacterDailyRefillCommandHandler refill,
        PublishSelectedCharacterSessionCommandHandler publish,
        MarkSelectedCharacterPvpReadyCommandHandler markPvpReady,
        SynchronizeSelectedCharacterQuestsCommandHandler quests,
        ReviveSelectedCharacterPetCommandHandler pet,
        CompensateSelectedCharacterSessionCommandHandler compensate) : ICommandBus
    {
        public SelectCharacterResult? State { get; private set; }
        public List<Type> DispatchedCommands { get; } = [];

        public async ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            DispatchedCommands.Add(command.GetType());
            object result = command switch
            {
                PrepareSelectedCharacterSessionCommand value =>
                    await RunAsync(prepare, value, cancellationToken),
                CleanupExpiredCharacterItemsCommand value =>
                    await RunAsync(cleanupExpired, value, cancellationToken),
                EnsureDailyContractPuppetsCommand => false,
                PrepareSelectedCharacterStateCommand value =>
                    await RunAsync(prepareState, value, cancellationToken),
                ApplySelectedCharacterDailyRefillCommand value =>
                    await RunAsync(refill, value, cancellationToken),
                PublishSelectedCharacterSessionCommand value =>
                    await RunAsync(publish, value, cancellationToken),
                MarkSelectedCharacterPvpReadyCommand value =>
                    await RunAsync(markPvpReady, value, cancellationToken),
                SynchronizeSelectedCharacterQuestsCommand value =>
                    await RunAsync(quests, value, cancellationToken),
                ReviveSelectedCharacterPetCommand value =>
                    await RunAsync(pet, value, cancellationToken),
                CompensateSelectedCharacterSessionCommand value =>
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

        private static ValueTask<TResult> RunAsync<TCommand, TResult>(
            ICommandHandler<TCommand, TResult> handler,
            TCommand command,
            CancellationToken cancellationToken)
            where TCommand : class, ICommand<TResult>
            where TResult : notnull =>
            handler.HandleAsync(
                new CommandExecutionContext(
                    Guid.NewGuid(),
                    command.GetType(),
                    handler.GetType(),
                    CommandExecutionKind.Workflow,
                    AccountAccessMode: UserAccessMode.Exclusive),
                command,
                cancellationToken);
    }

    private sealed class StubExpiredCleanup(bool refuse, bool fail) : IExpiredCharacterItemCleanupDomainService
    {
        public ValueTask<ExpiredItemCleanupResult> CleanupAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            fail ? ValueTask.FromException<ExpiredItemCleanupResult>(new IOException("database write failed"))
                : ValueTask.FromResult(new ExpiredItemCleanupResult(!refuse, []));
    }

    private sealed class RejectedLoginCommandBus : ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull =>
            ValueTask.FromResult((TResult)(object)new RedeemGameLoginResult(null));
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

    private sealed class QuestEntry(List<string> ledger, bool changes) : ISelectCharacterQuestEntryDomainService
    {
        public ValueTask<SelectCharacterQuestSynchronization?> SynchronizeAsync(
            SelectedSessionIdentity session, SelectCharacterResult result,
            CancellationToken cancellationToken)
        {
            ledger.Add("quest");
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

    private sealed class RecordingActive(IActiveCharacterDomainService inner, List<string> ledger)
        : IActiveCharacterDomainService
    {
        public void Publish(SelectedSessionIdentity session)
        {
            ledger.Add("active");
            inner.Publish(session);
        }

        public bool ClearExact(SelectedSessionIdentity session) => inner.ClearExact(session);
    }

    private sealed class RecordingCleanup(
        ISelectedSessionDomainService directory, IActiveCharacterDomainService active, List<string> ledger)
        : ISelectedSessionCleanupDomainService
    {
        public List<(SelectedSessionIdentity Session, bool IncludeOwner)> Calls { get; } = [];
        public List<SelectedSessionIdentity?> SelectedOwnersAtCall { get; } = [];
        public SelectedSessionIdentity? FailureSession { get; set; }

        public ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
            SelectedSessionIdentity session, bool includeOwner, CancellationToken cancellationToken)
        {
            ledger.Add("cleanup");
            Calls.Add((session, includeOwner));
            SelectedOwnersAtCall.Add(directory.Find(session.AccountId));
            if (FailureSession == session)
                return ValueTask.FromException<SelectedSessionCleanupNotificationPlan>(
                    new IOException("cleanup failed"));
            _ = active.ClearExact(session);
            _ = directory.ReleaseExact(session);
            return ValueTask.FromResult(SelectedSessionCleanupNotificationPlan.Empty(session));
        }
    }

    private sealed class RecordingSender(
        List<string> ledger, bool failTrailingLocks, bool failPvp,
        bool failPvpTarget, bool failStatic, bool blockStatic)
        : IGameMessageSender
    {
        public bool FailStatic { get; set; } = failStatic;
        private readonly TaskCompletionSource staticRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StaticEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseStatic() => staticRelease.TrySetResult();
        public int StateMessageCount { get; private set; }

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId, IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            SendClassifiedAsync(message);

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            SendClassifiedAsync(message);

        private ValueTask SendClassifiedAsync(IGameServerMessage message)
        {
            if (message.MessageType == ServerMessageType.GameStateSynchronization)
            {
                StateMessageCount++;
                if (StateMessageCount == 1)
                {
                    ledger.Add("static");
                    if (blockStatic)
                    {
                        StaticEntered.TrySetResult();
                        return AwaitStaticReleaseAsync();
                    }
                    if (FailStatic)
                        return ValueTask.FromException(new IOException("static delivery failed"));
                }
            }
            else ledger.Add("message");
            return ValueTask.CompletedTask;

            async ValueTask AwaitStaticReleaseAsync()
            {
                await staticRelease.Task;
                if (FailStatic)
                    throw new IOException("static delivery failed");
            }
        }

        public ValueTask SendExplicitAsync(
            NetworkConnectionId connectionId, GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            if (delivery.RequiredDirectMessages.Count == 0
                && delivery.BestEffortTargets.Count == 0)
                return ValueTask.CompletedTask;
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
            if (pvp && failPvp)
                return ValueTask.FromException(new IOException("PvP direct delivery failed"));
            // The Networking sender owns target isolation; a target transport failure is
            // represented here by a completed best-effort batch.
            _ = failPvpTarget;
            return trailing && failTrailingLocks
                ? ValueTask.FromException(new IOException("required delivery failed"))
                : ValueTask.CompletedTask;
        }
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
                            : NotificationOutcome.Delivered))
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
            IReadOnlyList<SelectedSessionIdentity> representedSessions,
            CancellationToken cancellationToken)
        {
            ledger.Add(
                $"party:{string.Join(',', recipients.Select(value => value.CharacterId.Value))}"
                + $"<-{string.Join(',', representedSessions.Select(value => value.CharacterId.Value))}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingCloser(List<string> ledger) : INetworkConnectionCloser
    {
        public List<NetworkConnectionId> Closed { get; } = [];
        public Func<ValueTask>? OnClose { get; set; }

        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken ct) => RequestCloseAsync(expected.ConnectionId, ct);
        public ValueTask RequestCloseAsync(
            NetworkConnectionId connectionId, CancellationToken cancellationToken)
        {
            ledger.Add("close");
            Closed.Add(connectionId);
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
