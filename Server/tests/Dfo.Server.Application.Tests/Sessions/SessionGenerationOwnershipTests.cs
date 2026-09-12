using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class SessionGenerationOwnershipTests
{
    [Fact]
    public void ExactCleanup_DoesNotRemoveReplacementOwnedRuntimeState()
    {
        var oldSession = Session("old", generation: 3);
        var current = Session("new", generation: 4);
        var presence = new SessionPresenceDomainService();
        var stores = new ExpertStoreDomainService();
        var lobby = new PvpLobbyDomainService();

        presence.Admit(new SessionPresence(current.CharacterId, 10011, 1, 2, true, current));
        stores.Open(current);
        lobby.MarkReady(new PvpLobbyParticipant(current.CharacterId, 10011, current));

        Assert.False(presence.WithdrawExact(oldSession));
        Assert.False(stores.CloseExact(oldSession));
        Assert.False(lobby.ClearReadyExact(oldSession));
        Assert.NotNull(presence.Find(current.CharacterId));
        Assert.True(stores.HasOpenStore(current.CharacterId));
        Assert.Single(lobby.ReadyOn(10011, new CharacterId(999)));

        Assert.True(presence.WithdrawExact(current));
        Assert.True(stores.CloseExact(current));
        Assert.True(lobby.ClearReadyExact(current));
    }

    [Fact]
    public async Task Cleanup_AttemptsLaterExactActionsWhenOneActionThrows()
    {
        var session = Session("cleanup", 9);
        var directory = new SelectedSessionDomainService();
        directory.Select(session);
        var service = new SelectedSessionCleanupService(
            directory, new SelectedSessionRuntimeDirectory(), new EmptyInventoryLease(), new CharacterContainerDomainService(),
            new CharacterItemCooldownDomainService(TimeProvider.System),
            new ThrowingActive(), new SessionPresenceDomainService(), new ExpertStoreDomainService(),
            new PvpLobbyDomainService(), [], new CharacterPositionDomainService(),
            new PassiveParties());

        await Assert.ThrowsAsync<SelectedSessionCleanupFailure>(async () =>
            await service.CleanupAsync(session, includeOwner: false, CancellationToken.None));

        Assert.Null(directory.Find(session.AccountId));
    }

    [Fact]
    public async Task CleanupFailure_PreservesAlreadyResolvedOwnerAndPeerNotifications()
    {
        var owner = Session("cleanup-owner", 9);
        var peer = SelectedSessionIdentity.Create(
            new AccountId(8), new CharacterId(80), new NetworkConnectionId("cleanup-peer"),
            1, 10011);
        var directory = new SelectedSessionDomainService();
        directory.Select(owner);
        directory.Select(peer);
        var presence = new SessionPresenceDomainService();
        presence.Admit(new SessionPresence(owner.CharacterId, 10011, 1, 2, true, owner));
        presence.Admit(new SessionPresence(peer.CharacterId, 10011, 1, 2, true, peer));
        var stores = new ExpertStoreDomainService();
        stores.Open(owner);
        var service = new SelectedSessionCleanupService(
            directory, new SelectedSessionRuntimeDirectory(), new EmptyInventoryLease(), new CharacterContainerDomainService(),
            new CharacterItemCooldownDomainService(TimeProvider.System),
            new ThrowingActive(), presence, stores, new PvpLobbyDomainService(),
            [], new CharacterPositionDomainService(), new PassiveParties());

        var failure = await Assert.ThrowsAsync<SelectedSessionCleanupFailure>(async () =>
            await service.CleanupAsync(owner, includeOwner: true, CancellationToken.None));

        Assert.Equal(
            [SelectedSessionCleanupNoticeKind.ExpertStoreClosed],
            failure.NotificationPlan.RequiredOwnerNotices.Select(value => value.Kind));
        Assert.Equal([peer.CharacterId], failure.NotificationPlan.PeerAudience);
        Assert.Equal(
            [SelectedSessionCleanupNoticeKind.ExpertStoreClosed,
                SelectedSessionCleanupNoticeKind.TownDeparture],
            failure.NotificationPlan.PeerNotices.Select(value => value.Kind));
        Assert.Null(directory.Find(owner.AccountId));
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
        var directory = new SelectedSessionDomainService();
        directory.Select(owner);
        directory.Select(peer2);
        directory.Select(peer1);
        directory.Select(otherListener);
        var presence = new SessionPresenceDomainService();
        presence.Admit(new SessionPresence(owner.CharacterId, 10011, 1, 2, true, owner));
        presence.Admit(new SessionPresence(peer2.CharacterId, 10011, 1, 2, true, peer2));
        presence.Admit(new SessionPresence(peer1.CharacterId, 10011, 1, 2, true, peer1));
        presence.Admit(new SessionPresence(
            otherListener.CharacterId, 10012, 1, 2, true, otherListener));
        var stores = new ExpertStoreDomainService();
        stores.Open(owner);
        var service = new SelectedSessionCleanupService(
            directory, new SelectedSessionRuntimeDirectory(), new EmptyInventoryLease(), new CharacterContainerDomainService(),
            new CharacterItemCooldownDomainService(TimeProvider.System),
            new PassiveActive(), presence, stores, new PvpLobbyDomainService(),
            [], new CharacterPositionDomainService(), new PassiveParties());

        var delivery = await service.CleanupAsync(owner, includeOwner, CancellationToken.None);

        Assert.Equal(includeOwner ? 1 : 0, delivery.RequiredOwnerNotices.Count);
        if (includeOwner)
            Assert.Equal(
                SelectedSessionCleanupNoticeKind.ExpertStoreClosed,
                delivery.RequiredOwnerNotices[0].Kind);
        Assert.Equal([peer1.CharacterId, peer2.CharacterId], delivery.PeerAudience);
        // One set for everybody rather than a copy each. They were always the same two entries;
        // saying so once is the difference between that being true and being a coincidence.
        Assert.Equal(
            [SelectedSessionCleanupNoticeKind.ExpertStoreClosed,
                SelectedSessionCleanupNoticeKind.TownDeparture],
            delivery.PeerNotices.Select(value => value.Kind));
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
        // Entry hands both of these over in one run; this stands in for that. What is saved is what
        // the position service holds, and whether it is saved at all is still the runtime's flag.
        var positions = new CharacterPositionDomainService();
        positions.Place(session, result.Character.Position);
        var directory = new SelectedSessionDomainService();
        directory.Select(session);
        var persistence = new RecordingPositionPersistence();
        var service = new SelectedSessionCleanupService(
            directory, runtimes, new EmptyInventoryLease(), new CharacterContainerDomainService(),
            new CharacterItemCooldownDomainService(TimeProvider.System),
            new PassiveActive(),
            new SessionPresenceDomainService(), new ExpertStoreDomainService(), new PvpLobbyDomainService(),
            [persistence], positions, new PassiveParties());

        await service.CleanupAsync(session, false, CancellationToken.None);

        Assert.Equal(expectedWrites, persistence.Writes);
    }

    [Fact]
    public async Task Cleanup_StaleSessionDoesNotProjectOrRemoveReplacementPresence()
    {
        var stale = Session("stale", 1);
        var replacement = Session("replacement", 2);
        var directory = new SelectedSessionDomainService();
        directory.Select(replacement);
        var presence = new SessionPresenceDomainService();
        presence.Admit(new SessionPresence(replacement.CharacterId, 10011, 1, 2, true, replacement));
        var stores = new ExpertStoreDomainService();
        stores.Open(replacement);
        var service = new SelectedSessionCleanupService(
            directory, new SelectedSessionRuntimeDirectory(), new EmptyInventoryLease(), new CharacterContainerDomainService(),
            new CharacterItemCooldownDomainService(TimeProvider.System),
            new PassiveActive(), presence, stores, new PvpLobbyDomainService(),
            [], new CharacterPositionDomainService(), new PassiveParties());

        var delivery = await service.CleanupAsync(
            stale, includeOwner: true, CancellationToken.None);

        Assert.Empty(delivery.RequiredOwnerNotices);
        Assert.Empty(delivery.PeerAudience);
        Assert.Empty(delivery.PeerNotices);
        Assert.Equal(replacement, directory.Find(replacement.AccountId));
        Assert.Equal(replacement, presence.Find(replacement.CharacterId)!.Session);
        Assert.True(stores.HasOpenStore(replacement.CharacterId));
    }

    /// <remarks>
    /// The cooldown record is memory the character owns, and its own contract says a relog clears
    /// every cooldown. Nothing but this wiring makes that true: without it a thirty-minute summon
    /// stays on cooldown across a re-entry, and the entries are never reclaimed.
    /// </remarks>
    [Fact]
    public async Task Cleanup_ClearsTheCharactersCooldowns()
    {
        var session = Session("cleanup-cooldown", 11);
        var directory = new SelectedSessionDomainService();
        directory.Select(session);
        var cooldowns = new CharacterItemCooldownDomainService(TimeProvider.System);
        cooldowns.Start(session.CharacterId, ItemCooldownKey.ForGroup(70), TimeSpan.FromMinutes(30));
        var service = new SelectedSessionCleanupService(
            directory, new SelectedSessionRuntimeDirectory(), new EmptyInventoryLease(),
            new CharacterContainerDomainService(), cooldowns,
            new PassiveActive(), new SessionPresenceDomainService(), new ExpertStoreDomainService(),
            new PvpLobbyDomainService(), [], new CharacterPositionDomainService(),
            new PassiveParties());

        await service.CleanupAsync(session, includeOwner: false, CancellationToken.None);

        Assert.False(
            cooldowns.IsCoolingDown(session.CharacterId, ItemCooldownKey.ForGroup(70)));
    }

    /// <remarks>
    /// A character who re-entered on another connection owns the record now, so the departing
    /// session must not clear it out from under them.
    /// </remarks>
    [Fact]
    public async Task Cleanup_LeavesTheCooldownsOfACharacterWhoAlreadyReturned()
    {
        var departing = Session("cooldown-departing", 12);
        var returned = Session("cooldown-returned", 13);
        var directory = new SelectedSessionDomainService();
        directory.Select(returned);
        var cooldowns = new CharacterItemCooldownDomainService(TimeProvider.System);
        cooldowns.Start(returned.CharacterId, ItemCooldownKey.ForGroup(70), TimeSpan.FromMinutes(30));
        var service = new SelectedSessionCleanupService(
            directory, new SelectedSessionRuntimeDirectory(), new EmptyInventoryLease(),
            new CharacterContainerDomainService(), cooldowns,
            new PassiveActive(), new SessionPresenceDomainService(), new ExpertStoreDomainService(),
            new PvpLobbyDomainService(), [], new CharacterPositionDomainService(),
            new PassiveParties());

        await service.CleanupAsync(departing, includeOwner: false, CancellationToken.None);

        Assert.True(
            cooldowns.IsCoolingDown(returned.CharacterId, ItemCooldownKey.ForGroup(70)));
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

    private sealed class ThrowingActive : IActiveCharacterDomainService
    {
        public void Publish(SelectedSessionIdentity session) { }
        public bool ClearExact(SelectedSessionIdentity session) => throw new InvalidOperationException("fault");
    }

    private sealed class PassiveActive : IActiveCharacterDomainService
    {
        public void Publish(SelectedSessionIdentity session) { }
        public bool ClearExact(SelectedSessionIdentity session) => false;
    }

    private sealed class PassiveParties : IPartyDomainService
    {
        public PartyOperationResult CreateOrUpdateSettingsExact(
            SelectedSessionIdentity session,
            PartySettings settings,
            PartyTitle title) => PartyOperationResult.Success();

        public PartyOperationResult UpdateEndpoint(
            SelectedSessionIdentity session,
            PartyEndpoint endpoint) => PartyOperationResult.Success();

        public PartyOperationResult RequestInvite(
            SelectedSessionIdentity inviter,
            ushort targetWireUserId,
            PartyInviteDisplayState display) => PartyOperationResult.Success();

        public PartyOperationResult AcceptInvite(
            SelectedSessionIdentity invitee,
            ushort inviterWireUserId) => PartyOperationResult.Success();

        public PartyOperationResult LeavePartyExact(SelectedSessionIdentity session) =>
            PartyOperationResult.Success();

        public PartyOperationResult CleanupSessionExact(SelectedSessionIdentity session) =>
            PartyOperationResult.Success();

        public PartySnapshot? FindParty(SelectedSessionIdentity session) => null;

        public PartyEndpoint? FindEndpoint(SelectedSessionIdentity session) => null;
    }

    private sealed class NoopSender : INetworkMessageSender
    {
        public ValueTask SendAsync(NetworkConnectionId connectionId, IServerNetworkMessage message,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId,
            ExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingSender : INetworkMessageSender
    {
        public List<ExplicitMessageDelivery> Deliveries { get; } = [];
        public ValueTask SendAsync(NetworkConnectionId connectionId, IServerNetworkMessage message,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId,
            ExplicitMessageDelivery delivery, CancellationToken cancellationToken)
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
            this.Writes++;
            return ValueTask.CompletedTask;
        }
    }
}
