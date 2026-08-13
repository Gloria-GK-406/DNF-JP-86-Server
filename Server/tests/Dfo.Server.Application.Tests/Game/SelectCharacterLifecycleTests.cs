using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Game;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Game;

public sealed class SelectCharacterLifecycleTests
{
    [Fact]
    public async Task Success_OrdersRequiredDeliveryBeforePublicationAndPostEntryStages()
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Handler.HandleAsync(
            fixture.Context, fixture.Command, CancellationToken.None);

        var spawn = fixture.Result.World.Channel.Spawn;
        Assert.Equal(
            new CharacterPositionState(spawn.TownId, spawn.AreaId, spawn.X, spawn.Y, spawn.Direction),
            result.Character.Position);
        Assert.Equal(result.Character.Position, fixture.Mutations.PersistedPosition);
        Assert.Equal(result.Character.Position, fixture.Sender.StaticResult!.Character.Position);
        Assert.Equal(
            ["prepare", "position", "static", "pre", "inventory", "active", "publish",
                "plan-pvp", "x11", "quest", "pet", "rejoin"],
            fixture.Ledger);
        Assert.NotNull(fixture.Directory.Find(fixture.AccountId));
        Assert.Equal(result.Character, fixture.Active.GetRequired(fixture.AccountId));
        Assert.Empty(fixture.Closer.Closed);
    }

    [Fact]
    public async Task RequiredOwnerFailure_CompensatesExactAttemptAndClosesOnlyItsConnection()
    {
        var fixture = await Fixture.CreateAsync(failTrailingLocks: true);

        await Assert.ThrowsAsync<IOException>(async () =>
            await fixture.Handler.HandleAsync(
                fixture.Context, fixture.Command, CancellationToken.None));

        Assert.Equal(
            ["prepare", "position", "static", "pre", "inventory", "active", "publish",
                "plan-pvp", "x11", "cleanup", "close"],
            fixture.Ledger);
        Assert.Null(fixture.Directory.Find(fixture.AccountId));
        Assert.Throws<KeyNotFoundException>(() => fixture.Active.GetRequired(fixture.AccountId));
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
        Assert.True(fixture.Users.TryGetByConnection(fixture.ConnectionId, out var retryUser));
        await fixture.Handler.HandleAsync(
            fixture.Context,
            fixture.Command with { ExpectedConnectionGeneration = retryUser!.Generation },
            CancellationToken.None);
        Assert.Equal(1, fixture.Inventory.Claims);
    }

    [Fact]
    public async Task QuestAndPetChangesReplaceThePublishedRuntimeSnapshot()
    {
        var fixture = await Fixture.CreateAsync(changeQuest: true, revivePet: true);

        var result = await fixture.Handler.HandleAsync(
            fixture.Context, fixture.Command, CancellationToken.None);

        Assert.Equal(3, Assert.Single(result.CharacterEntry.Quests.ActiveQuests).TriggerValue);
        Assert.Equal(1, Assert.Single(result.CharacterEntry.Features.Pets).Field04);
        Assert.Equal(result, fixture.SessionEntry.RuntimeResult);
        Assert.Equal(2, fixture.Ledger.Count(value => value == "runtime-update"));
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
            fixture.Sender, TimeProvider.System);
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
            fixture.Sender, TimeProvider.System);
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
            fixture.Sender, TimeProvider.System);
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
    public async Task AdmissionRefusal_SendsOrderedX08AndHasZeroLifecycleSideEffects()
    {
        var fixture = await Fixture.CreateAsync(refuseAdmission: true);

        await Assert.ThrowsAsync<Dfo.Server.Data.Channel.ChannelAdmissionRefusedException>(async () =>
            await fixture.Handler.HandleAsync(
                fixture.Context, fixture.Command, CancellationToken.None));

        Assert.Equal(["prepare", "x08"], fixture.Ledger);
        Assert.Null(fixture.Directory.Find(fixture.AccountId));
        Assert.Throws<KeyNotFoundException>(() => fixture.Active.GetRequired(fixture.AccountId));
        Assert.Empty(fixture.Closer.Closed);
    }

    [Fact]
    public async Task AdmissionRefusal_DoesNotSaveOrReleaseExistingDirtyLease()
    {
        var fixture = await Fixture.CreateAsync(refuseAdmission: true);
        var previous = SelectedSessionIdentity.Create(
            fixture.AccountId, fixture.Result.Character.Identity.CharacterId,
            fixture.ConnectionId, fixture.Command.ExpectedConnectionGeneration,
            fixture.Command.Channel.ListenerPort);
        fixture.Directory.RegisterReplacing(previous);

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
            ["prepare", "position", "static", "pre", "inventory", "active", "publish",
                "plan-pvp", "pvp", "cleanup", "close"],
            fixture.Ledger);
        Assert.DoesNotContain("ready", fixture.Ledger);
        Assert.Null(fixture.Directory.Find(fixture.AccountId));
        Assert.Throws<KeyNotFoundException>(() => fixture.Active.GetRequired(fixture.AccountId));
    }

    [Fact]
    public async Task PvpPeerFailure_IsolatedAfterReadyAndDoesNotPreventTrailingLocks()
    {
        var fixture = await Fixture.CreateAsync(planPvp: true, failPvpTarget: true);

        await fixture.Handler.HandleAsync(
            fixture.Context, fixture.Command, CancellationToken.None);

        Assert.Equal(
            ["prepare", "position", "static", "pre", "inventory", "active", "publish",
                "plan-pvp", "pvp", "ready", "target", "x11", "quest", "pet", "rejoin"],
            fixture.Ledger);
        Assert.NotNull(fixture.Directory.Find(fixture.AccountId));
        Assert.Empty(fixture.Closer.Closed);
    }

    [Fact]
    public async Task SameConnectionSwitch_RefreshesGenerationAfterNormalCleanup()
    {
        var fixture = await Fixture.CreateAsync();
        Assert.True(fixture.Users.TryGetByConnection(fixture.ConnectionId, out var before));
        var old = SelectedSessionIdentity.Create(
            fixture.AccountId, new CharacterId(500), fixture.ConnectionId,
            before!.Generation, StubChannelEntryPolicy.GeneralChannel.ListenerPort);
        fixture.Directory.RegisterReplacing(old);
        fixture.Active.Publish(old, fixture.Result.Character with
        {
            Identity = fixture.Result.Character.Identity with { CharacterId = old.CharacterId }
        });
        fixture.Ledger.Clear();
        Assert.True(fixture.Users.TryGetByConnection(fixture.ConnectionId, out var selected));

        await fixture.Handler.HandleAsync(
            fixture.Context, fixture.Command with { ExpectedConnectionGeneration = selected!.Generation },
            CancellationToken.None);

        var current = fixture.Directory.Find(fixture.AccountId)!;
        Assert.True(current.ConnectionGeneration > selected.Generation);
        Assert.Equal([(old, true)], fixture.Cleanup.Calls);
        Assert.DoesNotContain(fixture.ConnectionId, fixture.Closer.Closed);
        Assert.False(fixture.Directory.TryRemoveExact(old));
    }

    [Fact]
    public async Task CrossConnectionDisplacement_CleansWithoutOwnerAndClosesOldConnection()
    {
        var fixture = await Fixture.CreateAsync();
        var displaced = SelectedSessionIdentity.Create(
            fixture.AccountId, new CharacterId(500), new NetworkConnectionId("Game:old"),
            1, StubChannelEntryPolicy.GeneralChannel.ListenerPort);
        fixture.Directory.RegisterReplacing(displaced);
        fixture.Closer.OnClose = async () =>
        {
            await using var callbackLease = await fixture.TransitionGate.AcquireAsync(
                fixture.AccountId, UserAccessMode.Exclusive, CancellationToken.None);
        };

        await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([(displaced, false)], fixture.Cleanup.Calls);
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
        fixture.Directory.RegisterReplacing(displaced);
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
            ISelectedSessionDirectory directory,
            RecordingActive active,
            RecordingCloser closer,
            UserService users,
            RecordingCleanup cleanup,
            CharacterMutations mutations,
            InventoryPreparation inventory,
            RecordingSender sender,
            SessionEntry sessionEntry,
            Preparation? preparation,
            SelectedSessionTransitionGate transitionGate,
            SelectCharacterCommandHandler handler,
            SelectCharacterCommand command)
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
            SessionEntry = sessionEntry;
            Preparation = preparation;
            TransitionGate = transitionGate;
            Handler = handler;
            Command = command;
            Context = new CommandExecutionContext(
                Guid.NewGuid(), command.GetType(), handler.GetType(), CommandExecutionKind.Workflow);
        }

        public AccountId AccountId { get; }
        public NetworkConnectionId ConnectionId { get; }
        public SelectCharacterResult Result { get; }
        public List<string> Ledger { get; }
        public ISelectedSessionDirectory Directory { get; }
        public RecordingActive Active { get; }
        public RecordingCloser Closer { get; }
        public UserService Users { get; }
        public RecordingCleanup Cleanup { get; }
        public CharacterMutations Mutations { get; }
        public InventoryPreparation Inventory { get; }
        public RecordingSender Sender { get; }
        public SessionEntry SessionEntry { get; }
        public Preparation? Preparation { get; }
        public SelectedSessionTransitionGate TransitionGate { get; }
        public SelectCharacterCommandHandler Handler { get; }
        public SelectCharacterCommand Command { get; }
        public CommandExecutionContext Context { get; }

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
            bool failPreparation = false)
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
            var ledger = new List<string>();
            var users = new UserService(() => new AccountRepository(accountId));
            var registered = users.RegisterConnection(new NetworkConnectionContext(
                connectionId, NetworkEndpointKind.Game, StubChannelEntryPolicy.GeneralChannel.ListenerPort));
            await users.LoginAsync(
                registered.OnlineUserId, "lifecycle", "hash", CancellationToken.None);
            var authenticated = users.GetRequired(registered.OnlineUserId);
            var directory = new SelectedSessionDirectory();
            var active = new RecordingActive(users, ledger);
            var closer = new RecordingCloser(ledger);
            var cleanup = new RecordingCleanup(directory, active, ledger);
            var mutations = new CharacterMutations(ledger);
            var inventory = new InventoryPreparation(result, ledger);
            var sender = new RecordingSender(
                ledger, failTrailingLocks, failPvp, failPvpTarget, failStatic, blockStatic);
            var sessionEntry = new SessionEntry(ledger, planPvp);
            var transitionGate = new SelectedSessionTransitionGate();
            var preparation = refuseAdmission ? null : new Preparation(result, ledger, failPreparation);
            var handler = new SelectCharacterCommandHandler(
                users,
                refuseAdmission ? new RefusingPreparation(ledger) : preparation!,
                directory,
                cleanup,
                inventory,
                sessionEntry,
                new QuestEntry(ledger, changeQuest),
                new PetEntry(ledger, revivePet),
                active,
                mutations,
                transitionGate,
                sender,
                closer);
            var command = new SelectCharacterCommand(
                accountId, 3, StubChannelEntryPolicy.GeneralChannel,
                connectionId, authenticated.Generation);
            return new Fixture(
                accountId, connectionId, result, ledger, directory, active, closer,
                users, cleanup, mutations, inventory, sender, sessionEntry,
                preparation, transitionGate, handler, command);
        }
    }

    private sealed class AccountRepository(AccountId accountId) : IAccountRepository
    {
        public ValueTask<AccountRecord> GetOrCreateAsync(
            string loginId, string passwordHash, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AccountRecord(
                accountId, loginId, passwordHash, DateTimeOffset.UnixEpoch));
    }

    private sealed class Preparation(
        SelectCharacterResult result, List<string> ledger, bool failPreparation)
        : ISelectCharacterPreparationService
    {
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

    private sealed class RefusingPreparation(List<string> ledger) : ISelectCharacterPreparationService
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
    }

    private sealed class InventoryPreparation(SelectCharacterResult result, List<string> ledger)
        : ISelectCharacterEntryInventoryService
    {
        public int Claims { get; private set; }
        public int Saves { get; private set; }
        public ValueTask SaveExistingLeaseAsync(SelectedSessionIdentity previousSession,
            CancellationToken cancellationToken)
        {
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
                current with { CharacterInventory = result.CharacterInventory }, []));
        }
    }

    private sealed class SessionEntry(List<string> ledger, bool planPvp)
        : ISelectCharacterSessionEntryService
    {
        public SelectCharacterResult? RuntimeResult { get; private set; }
        public SelectCharacterPvpLobbyProjection? PlanPvp(
            SelectedSessionIdentity session, SelectCharacterResult result)
        {
            ledger.Add("plan-pvp");
            return planPvp
                ? new SelectCharacterPvpLobbyProjection(
                    [], [], new SelectCharacterPvpIdentityProjection(session.WireUserId, result))
                : null;
        }

        public IReadOnlyList<ExplicitProjectionTarget> PlanPvpPublication(
            SelectedSessionIdentity session, SelectCharacterResult result) => planPvp
            ? [new ExplicitProjectionTarget(new NetworkConnectionId("Game:peer"),
                [new SelectCharacterPvpIdentityProjection(session.WireUserId, result)])]
            : [];

        public void Publish(SelectedSessionIdentity session, SelectCharacterResult result)
        {
            RuntimeResult = result;
            ledger.Add("publish");
        }

        public void ReplaceRuntimeExact(SelectedSessionIdentity session, SelectCharacterResult result)
        {
            RuntimeResult = result;
            ledger.Add("runtime-update");
        }

        public void MarkPvpReady(SelectedSessionIdentity session, SelectCharacterResult result) =>
            ledger.Add("ready");

        public SelectCharacterDungeonRejoinProjection? PlanRejoin(
            SelectedSessionIdentity session, SelectCharacterResult result)
        {
            ledger.Add("rejoin");
            return null;
        }
    }

    private sealed class QuestEntry(List<string> ledger, bool changes) : ISelectCharacterQuestEntryService
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
                    updated, new SelectCharacterAcceptedQuestsProjection(
                        [new SelectCharacterAcceptedQuestProjection(77, 3)])));
            }
            return ValueTask.FromResult<SelectCharacterQuestSynchronization?>(null);
        }
    }

    private sealed class PetEntry(List<string> ledger, bool changes) : ISelectCharacterPetEntryService
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
                    updated, new SelectCharacterPetRevivalProjection(session.WireUserId, 88, 1)));
            }
            return ValueTask.FromResult<SelectCharacterPetRevival?>(null);
        }
    }

    private sealed class CharacterMutations(List<string> ledger) : ICharacterEntryMutationRepository
    {
        public CharacterPositionState? PersistedPosition { get; private set; }
        public ValueTask<IReadOnlyList<SeekingQuestTriggerUpdate>> UpdateSeekingQuestTriggersAsync(
            AccountId accountId, CharacterId characterId,
            IReadOnlyList<SeekingQuestTriggerUpdate> updates, CancellationToken cancellationToken) =>
            ValueTask.FromResult(updates);

        public ValueTask<PetRevivalMutation?> ReviveEquippedCreatureAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<PetRevivalMutation?>(null);

        public ValueTask PersistPositionAsync(
            AccountId accountId, CharacterId characterId, CharacterPositionState position,
            CancellationToken cancellationToken)
        {
            ledger.Add("position");
            PersistedPosition = position;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingActive(IActiveCharacterService inner, List<string> ledger)
        : IActiveCharacterService
    {
        public void Publish(AccountId accountId, ActiveCharacterState character) =>
            inner.Publish(accountId, character);

        public void Publish(SelectedSessionIdentity session, ActiveCharacterState character)
        {
            ledger.Add("active");
            inner.Publish(session, character);
        }

        public ActiveCharacterState GetRequired(AccountId accountId) => inner.GetRequired(accountId);
        public bool ClearExact(SelectedSessionIdentity session) => inner.ClearExact(session);
    }

    private sealed class RecordingCleanup(
        ISelectedSessionDirectory directory, IActiveCharacterService active, List<string> ledger)
        : ISelectedSessionCleanupService
    {
        public List<(SelectedSessionIdentity Session, bool IncludeOwner)> Calls { get; } = [];
        public SelectedSessionIdentity? FailureSession { get; set; }

        public ValueTask CleanupAsync(
            SelectedSessionIdentity session, bool includeOwner, CancellationToken cancellationToken)
        {
            ledger.Add("cleanup");
            Calls.Add((session, includeOwner));
            if (FailureSession == session)
                return ValueTask.FromException(new IOException("cleanup failed"));
            _ = active.ClearExact(session);
            _ = directory.TryRemoveExact(session);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSender(
        List<string> ledger, bool failTrailingLocks, bool failPvp,
        bool failPvpTarget, bool failStatic, bool blockStatic)
        : INetworkMessageSender
    {
        public bool FailStatic { get; set; } = failStatic;
        private readonly TaskCompletionSource staticRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StaticEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseStatic() => staticRelease.TrySetResult();
        public SelectCharacterResult? StaticResult { get; private set; }

        public ValueTask SendAsync(
            NetworkConnectionId connectionId, IServerNetworkMessage message,
            CancellationToken cancellationToken)
        {
            if (message is SelectCharacterResponse response)
            {
                StaticResult = response.Result;
                ledger.Add("static");
                if (blockStatic)
                {
                    StaticEntered.TrySetResult();
                    return AwaitStaticReleaseAsync();
                }
                if (FailStatic)
                    return ValueTask.FromException(new IOException("static delivery failed"));
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

        public ValueTask SendProjectionAsync(
            NetworkConnectionId connectionId, SelectCharacterProjectionDelivery delivery,
            CancellationToken cancellationToken)
        {
            var trailing = delivery.RequiredDirectProjections
                .OfType<SelectCharacterTrailingEquipmentLocksProjection>().Any();
            var pvp = delivery.RequiredDirectProjections
                .OfType<SelectCharacterPvpLobbyProjection>().Any();
            var rejection = delivery.RequiredDirectProjections
                .OfType<SelectCharacterAdmissionRejectionProjection>().Any();
            var target = delivery.BestEffortTargets.Count > 0;
            ledger.Add(rejection ? "x08" : pvp ? "pvp" : target ? "target" : trailing ? "x11" : "pre");
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

    private sealed class RecordingCloser(List<string> ledger) : INetworkConnectionCloser
    {
        public List<NetworkConnectionId> Closed { get; } = [];
        public Func<ValueTask>? OnClose { get; set; }

        public ValueTask CloseAsync(
            NetworkConnectionId connectionId, CancellationToken cancellationToken)
        {
            ledger.Add("close");
            Closed.Add(connectionId);
            return OnClose?.Invoke() ?? ValueTask.CompletedTask;
        }
    }
}
