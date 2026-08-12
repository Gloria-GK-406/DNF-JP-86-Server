using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Game;
using Dfo.Server.Application.Tests.Characters;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class SessionGenerationOwnershipTests
{
    [Fact]
    public void ExactCleanup_DoesNotRemoveReplacementOwnedRuntimeState()
    {
        var oldSession = Session("old", generation: 3);
        var current = Session("new", generation: 4);
        var presence = new SessionPresenceRegistry();
        var stores = new ExpertStoreRegistry();
        var lobby = new PvpLobbyRegistry();
        var rejoin = new DungeonRejoinRegistry();

        presence.Admit(new SessionPresence(current.CharacterId, 10011, 1, 2, true, current));
        stores.Open(current);
        lobby.MarkReady(new PvpLobbyParticipant(current.CharacterId, 10011, current));
        rejoin.Attach(new DungeonAttachment(
            current.CharacterId, current.AccountId, 7, [current.WireUserId], true,
            DateTimeOffset.MaxValue, DateTimeOffset.MaxValue, current));

        Assert.False(presence.WithdrawExact(oldSession));
        Assert.False(stores.CloseExact(oldSession));
        Assert.False(lobby.ClearReadyExact(oldSession));
        Assert.False(rejoin.ReleaseExact(oldSession));
        Assert.NotNull(presence.Find(current.CharacterId));
        Assert.True(stores.HasOpenStore(current.CharacterId));
        Assert.Single(lobby.ReadyOn(10011, new CharacterId(999)));
        Assert.NotNull(rejoin.Find(current.CharacterId));

        Assert.True(presence.WithdrawExact(current));
        Assert.True(stores.CloseExact(current));
        Assert.True(lobby.ClearReadyExact(current));
        Assert.True(rejoin.ReleaseExact(current));
    }

    [Fact]
    public async Task Cleanup_AttemptsLaterExactActionsWhenOneActionThrows()
    {
        var session = Session("cleanup", 9);
        var directory = new SelectedSessionDirectory();
        directory.RegisterReplacing(session);
        var service = new SelectedSessionCleanupService(
            directory, new SelectedSessionRuntimeDirectory(), new EmptyInventoryLease(),
            new ThrowingActive(), new SessionPresenceRegistry(), new ExpertStoreRegistry(),
            new PvpLobbyRegistry(), new DungeonRejoinRegistry(), [], new NoopSender());

        await Assert.ThrowsAsync<AggregateException>(async () =>
            await service.CleanupAsync(session, includeOwner: false, CancellationToken.None));

        Assert.Null(directory.Find(session.AccountId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cleanup_ProjectsOwnerStoreCloseAndOrderedPeerStoreThenTownLeave(bool includeOwner)
    {
        var owner = Session("owner", 9);
        var peer1 = SelectedSessionIdentity.Create(new AccountId(8), new CharacterId(80),
            new NetworkConnectionId("peer-1"), 1, 10011);
        var peer2 = SelectedSessionIdentity.Create(new AccountId(9), new CharacterId(90),
            new NetworkConnectionId("peer-2"), 1, 10011);
        var otherListener = SelectedSessionIdentity.Create(new AccountId(10), new CharacterId(100),
            new NetworkConnectionId("peer-other-port"), 1, 10012);
        var directory = new SelectedSessionDirectory();
        directory.RegisterReplacing(owner);
        directory.RegisterReplacing(peer2);
        directory.RegisterReplacing(peer1);
        directory.RegisterReplacing(otherListener);
        var presence = new SessionPresenceRegistry();
        presence.Admit(new SessionPresence(owner.CharacterId, 10011, 1, 2, true, owner));
        presence.Admit(new SessionPresence(peer2.CharacterId, 10011, 1, 2, true, peer2));
        presence.Admit(new SessionPresence(peer1.CharacterId, 10011, 1, 2, true, peer1));
        presence.Admit(new SessionPresence(
            otherListener.CharacterId, 10012, 1, 2, true, otherListener));
        var stores = new ExpertStoreRegistry();
        stores.Open(owner);
        var sender = new RecordingSender();
        var service = new SelectedSessionCleanupService(
            directory, new SelectedSessionRuntimeDirectory(), new EmptyInventoryLease(),
            new PassiveActive(), presence, stores, new PvpLobbyRegistry(),
            new DungeonRejoinRegistry(), [], sender);

        await service.CleanupAsync(owner, includeOwner, CancellationToken.None);

        var delivery = Assert.Single(sender.Deliveries);
        Assert.Equal(includeOwner ? 1 : 0, delivery.RequiredDirectProjections.Count);
        if (includeOwner)
            Assert.IsType<SelectCharacterExpertStoreCloseProjection>(delivery.RequiredDirectProjections[0]);
        Assert.Equal([peer1.ConnectionId, peer2.ConnectionId],
            delivery.BestEffortTargets.Select(value => value.ConnectionId));
        Assert.All(delivery.BestEffortTargets, target => Assert.Collection(target.OrderedProjections,
            value => Assert.IsType<SelectCharacterExpertStoreCloseProjection>(value),
            value => Assert.IsType<SelectCharacterTownLeaveProjection>(value)));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task Cleanup_PersistsRuntimePositionOnlyWhenEntryPolicyAllowsIt(
        bool shouldPersist, int expectedWrites)
    {
        var session = Session("position", 9);
        var result = await CharacterEntryInventoryTests.CreateLifecycleResultAsync(
            session.AccountId, session.CharacterId.Value, 1);
        var runtimes = new SelectedSessionRuntimeDirectory();
        runtimes.Publish(session, result, shouldPersist);
        var directory = new SelectedSessionDirectory();
        directory.RegisterReplacing(session);
        var persistence = new RecordingPositionPersistence();
        var service = new SelectedSessionCleanupService(
            directory, runtimes, new EmptyInventoryLease(), new PassiveActive(),
            new SessionPresenceRegistry(), new ExpertStoreRegistry(), new PvpLobbyRegistry(),
            new DungeonRejoinRegistry(), [persistence], new NoopSender());

        await service.CleanupAsync(session, false, CancellationToken.None);

        Assert.Equal(expectedWrites, persistence.Writes);
    }

    [Fact]
    public async Task Cleanup_StaleSessionDoesNotProjectOrRemoveReplacementPresence()
    {
        var stale = Session("stale", 1);
        var replacement = Session("replacement", 2);
        var directory = new SelectedSessionDirectory();
        directory.RegisterReplacing(replacement);
        var presence = new SessionPresenceRegistry();
        presence.Admit(new SessionPresence(replacement.CharacterId, 10011, 1, 2, true, replacement));
        var stores = new ExpertStoreRegistry();
        stores.Open(replacement);
        var sender = new RecordingSender();
        var service = new SelectedSessionCleanupService(
            directory, new SelectedSessionRuntimeDirectory(), new EmptyInventoryLease(),
            new PassiveActive(), presence, stores, new PvpLobbyRegistry(),
            new DungeonRejoinRegistry(), [], sender);

        await service.CleanupAsync(stale, includeOwner: true, CancellationToken.None);

        Assert.Empty(sender.Deliveries);
        Assert.Equal(replacement, directory.Find(replacement.AccountId));
        Assert.Equal(replacement, presence.Find(replacement.CharacterId)!.Session);
        Assert.True(stores.HasOpenStore(replacement.CharacterId));
    }

    private static SelectedSessionIdentity Session(string connection, long generation) =>
        SelectedSessionIdentity.Create(
            new AccountId(7), new CharacterId(70), new NetworkConnectionId(connection),
            generation, 10011);

    private sealed class EmptyInventoryLease : IEntryInventoryLeaseRegistry
    {
        public EntryInventoryLeaseSnapshot? Find(AccountId accountId) => null;
        public bool TryMarkDirtyExact(SelectedSessionIdentity session,
            CharacterItemLeaseState inventory) => false;
        public void Replace(SelectedSessionIdentity session,
            Dfo.Server.Data.Inventory.CharacterInventoryEntryState inventory) { }
        public bool ReleaseExact(SelectedSessionIdentity session) => false;
    }

    private sealed class ThrowingActive : IActiveCharacterService
    {
        public void Publish(AccountId accountId, ActiveCharacterState character) { }
        public void Publish(SelectedSessionIdentity session, ActiveCharacterState character) { }
        public ActiveCharacterState GetRequired(AccountId accountId) => throw new KeyNotFoundException();
        public bool ClearExact(SelectedSessionIdentity session) => throw new InvalidOperationException("fault");
    }

    private sealed class PassiveActive : IActiveCharacterService
    {
        public void Publish(AccountId accountId, ActiveCharacterState character) { }
        public void Publish(SelectedSessionIdentity session, ActiveCharacterState character) { }
        public ActiveCharacterState GetRequired(AccountId accountId) => throw new KeyNotFoundException();
        public bool ClearExact(SelectedSessionIdentity session) => false;
    }

    private sealed class NoopSender : INetworkMessageSender
    {
        public ValueTask SendAsync(NetworkConnectionId connectionId, IServerNetworkMessage message,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class RecordingSender : INetworkMessageSender
    {
        public List<SelectCharacterProjectionDelivery> Deliveries { get; } = [];
        public ValueTask SendAsync(NetworkConnectionId connectionId, IServerNetworkMessage message,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask SendProjectionAsync(NetworkConnectionId requiredDirectConnectionId,
            SelectCharacterProjectionDelivery delivery, CancellationToken cancellationToken)
        {
            Deliveries.Add(delivery);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPositionPersistence : ISelectedSessionPositionPersistence
    {
        public int Writes { get; private set; }
        public ValueTask PersistAsync(AccountId accountId, CharacterId characterId,
            Dfo.Server.Data.Characters.CharacterPositionState position,
            CancellationToken cancellationToken)
        {
            Writes++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopMutations : ICharacterEntryMutationRepository
    {
        public ValueTask<IReadOnlyList<SeekingQuestTriggerUpdate>> UpdateSeekingQuestTriggersAsync(
            AccountId accountId, CharacterId characterId, IReadOnlyList<SeekingQuestTriggerUpdate> updates,
            CancellationToken cancellationToken) => ValueTask.FromResult(updates);
        public ValueTask<PetRevivalMutation?> ReviveEquippedCreatureAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<PetRevivalMutation?>(null);
        public ValueTask PersistPositionAsync(AccountId accountId, CharacterId characterId,
            Dfo.Server.Data.Characters.CharacterPositionState position,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
