using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Testing;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Sessions;

[Trait("Category", "Unit")]
public sealed class SessionGenerationOwnershipTests
{
    [Fact]
    public void ExactCleanup_DoesNotRemoveReplacementOwnedRuntimeState()
    {
        TestGameplay.Run(() =>
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
        });
    }

    [Fact]
    public async Task Cleanup_AttemptsLaterExactActionsWhenOneActionThrows()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = Session("cleanup", 9);
            var directory = Dfo.Server.Testing.TestSessions.Directory();
            directory.Select(session);
            var service = Cleanup(directory, runtime: new ThrowingRuntime());

            await Assert.ThrowsAsync<SelectedSessionCleanupFailure>(async () =>
                await TestEffectFootprints.Run(session, cap => service.CleanupAsync(session, includeOwner: false, cap, CancellationToken.None)));

            Assert.Null(directory.Find(session.AccountId));
        });
    }

    [Fact]
    public async Task CleanupFailure_PreservesAlreadyResolvedOwnerAndPeerNotifications()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var owner = Session("cleanup-owner", 9);
            var directory = Dfo.Server.Testing.TestSessions.Directory(out var state);
            directory.Select(owner);
            var peer = directory.InWorld(state, new AccountId(8), new CharacterId(80), "cleanup-peer", 10011);
            var presence = new SessionPresenceDomainService();
            presence.Admit(new SessionPresence(owner.CharacterId, 10011, 1, 2, true, owner));
            presence.Admit(new SessionPresence(peer.CharacterId, 10011, 1, 2, true, peer));
            var stores = new ExpertStoreDomainService();
            stores.Open(owner);
            var service = Cleanup(directory, presence, stores, runtime: new ThrowingRuntime());

            var failure = await Assert.ThrowsAsync<SelectedSessionCleanupFailure>(async () =>
                await TestEffectFootprints.Run(owner, cap => service.CleanupAsync(owner, includeOwner: true, cap, CancellationToken.None)));

            Assert.Equal(
                [SelectedSessionCleanupNoticeKind.ExpertStoreClosed],
                failure.NotificationPlan.RequiredOwnerNotices.Select(value => value.Kind));
            Assert.Equal(
                [peer.CharacterId],
                failure.NotificationPlan.PeerAudience.Select(value => value.CharacterId));
            Assert.Equal(
                [SelectedSessionCleanupNoticeKind.ExpertStoreClosed,
                    SelectedSessionCleanupNoticeKind.TownDeparture],
                failure.NotificationPlan.PeerNotices.Select(value => value.Kind));
            Assert.Null(directory.Find(owner.AccountId));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cleanup_ProjectsOwnerStoreCloseAndOrderedPeerStoreThenTownLeave(bool includeOwner)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var owner = Session("owner", 9);
            var directory = Dfo.Server.Testing.TestSessions.Directory(out var state);
            directory.Select(owner);
            var peer2 = directory.InWorld(state, new AccountId(9), new CharacterId(90), "peer-2", 10011);
            var peer1 = directory.InWorld(state, new AccountId(8), new CharacterId(80), "peer-1", 10011);
            var otherListener = directory.InWorld(
                state, new AccountId(10), new CharacterId(100), "peer-other-port", 10012);
            var presence = new SessionPresenceDomainService();
            presence.Admit(new SessionPresence(owner.CharacterId, 10011, 1, 2, true, owner));
            presence.Admit(new SessionPresence(peer2.CharacterId, 10011, 1, 2, true, peer2));
            presence.Admit(new SessionPresence(peer1.CharacterId, 10011, 1, 2, true, peer1));
            presence.Admit(new SessionPresence(
                otherListener.CharacterId, 10012, 1, 2, true, otherListener));
            var stores = new ExpertStoreDomainService();
            stores.Open(owner);
            var service = Cleanup(directory, presence, stores);

            var delivery = await TestEffectFootprints.Run(owner, cap => service.CleanupAsync(owner, includeOwner, cap, CancellationToken.None));

            Assert.Equal(includeOwner ? 1 : 0, delivery.RequiredOwnerNotices.Count);
            if (includeOwner)
                Assert.Equal(
                    SelectedSessionCleanupNoticeKind.ExpertStoreClosed,
                    delivery.RequiredOwnerNotices[0].Kind);
            Assert.Equal(
                [peer1.CharacterId, peer2.CharacterId],
                delivery.PeerAudience.Select(value => value.CharacterId));
            // One set for everybody rather than a copy each. They were always the same two entries;
            // saying so once is the difference between that being true and being a coincidence.
            Assert.Equal(
                [SelectedSessionCleanupNoticeKind.ExpertStoreClosed,
                    SelectedSessionCleanupNoticeKind.TownDeparture],
                delivery.PeerNotices.Select(value => value.Kind));
        });
    }

    /// <remarks>
    /// Whether a position is kept is the channel's answer, asked when the session ends; what is
    /// kept is what the online character runtime holds for that exact session.
    /// </remarks>
    [Theory]
    [InlineData(ChannelEntryKind.General, 1)]
    [InlineData(ChannelEntryKind.Raid, 0)]
    public async Task Cleanup_PersistsRuntimePositionOnlyWhenTheChannelKeepsPositions(
        ChannelEntryKind kind, int expectedWrites)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = Session("position", 9);
            var result = await CharacterEntryInventoryTests.CreateLifecycleResultAsync(
                session.AccountId, session.CharacterId.Value, 1);
            var runtime = new OnlineCharacterRuntimeDomainService(TimeProvider.System);
            runtime.Begin(session, result.Character.Position, 100);
            var directory = Dfo.Server.Testing.TestSessions.Directory();
            directory.Select(session);
            var persistence = new RecordingPositionPersistence();
            var service = Cleanup(directory, runtime: runtime, channels: new KindChannels(kind),
                persistence: persistence);

            await TestEffectFootprints.Run(session, cap => service.CleanupAsync(session, false, cap, CancellationToken.None));

            Assert.Equal(expectedWrites, persistence.Writes);
            if (expectedWrites == 1)
                Assert.Equal(result.Character.Position, persistence.Position);
        });
    }

    [Fact]
    public async Task Cleanup_DoesNotPersistAPositionForASessionThatNeverEnteredTheWorld()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = Session("never-entered", 9);
            var directory = Dfo.Server.Testing.TestSessions.Directory();
            directory.Select(session);
            var persistence = new RecordingPositionPersistence();
            var channels = new KindChannels(ChannelEntryKind.General);
            var service = Cleanup(directory, channels: channels, persistence: persistence);

            await TestEffectFootprints.Run(session, cap => service.CleanupAsync(session, false, cap, CancellationToken.None));

            Assert.Equal(0, persistence.Writes);
            Assert.Equal(0, channels.Resolutions);
        });
    }

    [Fact]
    public async Task Cleanup_ReportsAChannelItCannotResolveAndFinishesTheRest()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = Session("channel-fails", 9);
            var runtime = new OnlineCharacterRuntimeDomainService(TimeProvider.System);
            runtime.Begin(session, new CharacterPositionState(1, 0, 10, 10, 0), 100);
            var directory = Dfo.Server.Testing.TestSessions.Directory();
            directory.Select(session);
            var persistence = new RecordingPositionPersistence();
            var service = Cleanup(directory, runtime: runtime, channels: new FailingChannels(),
                persistence: persistence);

            var failure = await Assert.ThrowsAsync<SelectedSessionCleanupFailure>(async () =>
                await TestEffectFootprints.Run(session, cap => service.CleanupAsync(session, false, cap, CancellationToken.None)));

            Assert.Contains(failure.InnerExceptions, error => error.Message == "channel unavailable");
            Assert.Equal(0, persistence.Writes);
            Assert.Null(runtime.PositionOf(session.CharacterId));
            Assert.Null(directory.Find(session.AccountId));
        });
    }

    [Fact]
    public async Task Cleanup_StaleSessionDoesNotProjectOrRemoveReplacementPresence()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var stale = Session("stale", 1);
            var replacement = Session("replacement", 2);
            var directory = Dfo.Server.Testing.TestSessions.Directory();
            directory.Select(replacement);
            var presence = new SessionPresenceDomainService();
            presence.Admit(new SessionPresence(replacement.CharacterId, 10011, 1, 2, true, replacement));
            var stores = new ExpertStoreDomainService();
            stores.Open(replacement);
            var runs = new DungeonRunDomainService();
            var replacementRun = runs.Begin(
                replacement.CharacterId,
                new DungeonLayout(
                    10000, 0, 1, 1, new MazeCell(0, 0), new MazeCell(0, 0),
                    61000, 1, new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(61000)}),
                difficulty: 0);
            var departure = new RecordingDepartureSink();
            var service = Cleanup(directory, presence, stores, runs: runs, departures: [departure]);

            var delivery = await TestEffectFootprints.Run(stale, cap => service.CleanupAsync(
                stale, includeOwner: true, cap, CancellationToken.None));

            Assert.Empty(delivery.RequiredOwnerNotices);
            Assert.Empty(delivery.PeerAudience);
            Assert.Empty(delivery.PeerNotices);
            Assert.Equal(replacement, directory.Find(replacement.AccountId));
            Assert.Equal(replacement, presence.Find(replacement.CharacterId)!.Session);
            Assert.True(stores.HasOpenStore(replacement.CharacterId));
            Assert.Equal(replacementRun.RunId, runs.Find(replacement.CharacterId)!.RunId);
            Assert.Empty(departure.Calls);
        });
    }

    /// <remarks>
    /// The cooldown record is memory the character owns, and its own contract says a relog clears
    /// every cooldown. Nothing but this wiring makes that true: without it a thirty-minute summon
    /// stays on cooldown across a re-entry, and the entries are never reclaimed.
    /// </remarks>
    [Fact]
    public async Task Cleanup_ClearsTheCharactersCooldowns()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = Session("cleanup-cooldown", 11);
            var directory = Dfo.Server.Testing.TestSessions.Directory();
            directory.Select(session);
            var runtime = new OnlineCharacterRuntimeDomainService(TimeProvider.System);
            runtime.Begin(session, new CharacterPositionState(1, 0, 0, 0, 0), 100);
            runtime.StartCooldown(session, ItemCooldownKey.ForGroup(70), TimeSpan.FromMinutes(30));
            runtime.ApplySpiritPotion(session, 1, TimeSpan.FromHours(2));
            var service = Cleanup(directory, runtime: runtime, channels: new KindChannels(ChannelEntryKind.Raid));

            await TestEffectFootprints.Run(session, cap => service.CleanupAsync(session, includeOwner: false, cap, CancellationToken.None));

            Assert.False(
                runtime.IsCoolingDown(session.CharacterId, ItemCooldownKey.ForGroup(70)));
            Assert.Equal(0, runtime.SpiritPotionBonusRateOf(session.CharacterId));
        });
    }

    /// <remarks>
    /// A character who re-entered on another connection owns the record now, so the departing
    /// session must not clear it out from under them.
    /// </remarks>
    [Fact]
    public async Task Cleanup_LeavesTheCooldownsOfACharacterWhoAlreadyReturned()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var departing = Session("cooldown-departing", 12);
            var returned = Session("cooldown-returned", 13);
            var directory = Dfo.Server.Testing.TestSessions.Directory();
            directory.Select(returned);
            var runtime = new OnlineCharacterRuntimeDomainService(TimeProvider.System);
            runtime.Begin(returned, new CharacterPositionState(1, 0, 0, 0, 0), 100);
            runtime.StartCooldown(returned, ItemCooldownKey.ForGroup(70), TimeSpan.FromMinutes(30));
            runtime.ApplySpiritPotion(returned, 1, TimeSpan.FromHours(2));
            var service = Cleanup(directory, runtime: runtime);

            await TestEffectFootprints.Run(departing, cap => service.CleanupAsync(departing, includeOwner: false, cap, CancellationToken.None));

            Assert.True(
                runtime.IsCoolingDown(returned.CharacterId, ItemCooldownKey.ForGroup(70)));
            Assert.Equal(1, runtime.SpiritPotionBonusRateOf(returned.CharacterId));
        });
    }

    private static SelectedSessionIdentity Session(string connection, long generation) =>
        SelectedSessionIdentity.Create(
            new AccountId(7), new CharacterId(70), new NetworkConnectionId(connection),
            generation, 10011);

    private static SelectedSessionCleanupService Cleanup(
        SelectedSessionDomainService directory,
        SessionPresenceDomainService? presence = null,
        ExpertStoreDomainService? stores = null,
        IOnlineCharacterRuntimeDomainService? runtime = null,
        IChannelEntryDomainService? channels = null,
        RecordingPositionPersistence? persistence = null,
        DungeonRunDomainService? runs = null,
        IEnumerable<IDungeonRunParticipantDepartureSink>? departures = null) =>
        new(directory, new CharacterContainerDomainService(),
            presence ?? new SessionPresenceDomainService(), stores ?? new ExpertStoreDomainService(),
            new PvpLobbyDomainService(), persistence is null ? [] : [persistence],
            runtime ?? new OnlineCharacterRuntimeDomainService(TimeProvider.System),
            channels ?? new StubChannelEntryPolicy(),
            new PassiveParties(), runs ?? new DungeonRunDomainService(), departures ?? []);

    /// <summary>Ending the online character's record fails; everything after it still runs.</summary>
    private sealed class ThrowingRuntime : IOnlineCharacterRuntimeDomainService
    {
        private readonly OnlineCharacterRuntimeDomainService inner = new(TimeProvider.System);

        public void Begin(SelectedSessionIdentity session, CharacterPositionState position, byte hpPercent) =>
            inner.Begin(session, position, hpPercent);
        public bool EndExact(SelectedSessionIdentity session) => throw new InvalidOperationException("fault");
        public CharacterPositionState? PositionOf(SelectedSessionIdentity session) => inner.PositionOf(session);
        public CharacterPositionState? PositionOf(CharacterId characterId) => inner.PositionOf(characterId);
        public CharacterPositionState CrossInto(SelectedSessionIdentity session, byte townId, byte areaId, short x, short y) =>
            inner.CrossInto(session, townId, areaId, x, y);
        public CharacterPositionState StepTo(SelectedSessionIdentity session, short x, short y, byte direction) =>
            inner.StepTo(session, x, y, direction);
        public byte HpPercentOf(CharacterId characterId) => inner.HpPercentOf(characterId);
        public bool IsCoolingDown(CharacterId characterId, ItemCooldownKey key) => inner.IsCoolingDown(characterId, key);
        public void StartCooldown(SelectedSessionIdentity session, ItemCooldownKey key, TimeSpan duration) =>
            inner.StartCooldown(session, key, duration);
        public void ApplySpiritPotion(SelectedSessionIdentity session, double bonusRate, TimeSpan duration) =>
            inner.ApplySpiritPotion(session, bonusRate, duration);
        public double SpiritPotionBonusRateOf(CharacterId characterId) => inner.SpiritPotionBonusRateOf(characterId);
    }

    /// <summary>Every listener resolves to a channel of one kind.</summary>
    private sealed class KindChannels(ChannelEntryKind kind) : IChannelEntryDomainService
    {
        public ValueTask<Dfo.Server.Data.Channel.ChannelLoginEntry> ResolveLoginAsync(int listenerPort,CancellationToken ct) =>
            ValueTask.FromException<Dfo.Server.Data.Channel.ChannelLoginEntry>(new NotSupportedException("This channel-identity fixture does not supply login deployment facts."));

        public int Resolutions { get; private set; }

        public ValueTask<ChannelEntryIdentity> ResolveAsync(int listenerPort, CancellationToken cancellationToken)
        {
            Resolutions++;
            return ValueTask.FromResult(new ChannelEntryIdentity(11, listenerPort, kind));
        }

        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) => null;
    }

    private sealed class FailingChannels : IChannelEntryDomainService
    {
        public ValueTask<Dfo.Server.Data.Channel.ChannelLoginEntry> ResolveLoginAsync(int listenerPort,CancellationToken ct) =>
            ValueTask.FromException<Dfo.Server.Data.Channel.ChannelLoginEntry>(new NotSupportedException("This channel-identity fixture does not supply login deployment facts."));

        public ValueTask<ChannelEntryIdentity> ResolveAsync(int listenerPort, CancellationToken cancellationToken) =>
            ValueTask.FromException<ChannelEntryIdentity>(new InvalidOperationException("channel unavailable"));

        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) => null;
    }

    private sealed class PassiveParties : IPartyService
    {
        public PartyMembershipSnapshot? FindByKey(PartyKey key) => null;
        public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope) { }

        public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => [];

        public PartyOperationResult CreateOrUpdateSettings(
            PartyCharacterSnapshot character,
            PartySettings settings,
            PartyTitle title) => PartyOperationResult.Success();

        public PartyOperationResult RequestInvite(
            PartyCharacterSnapshot inviter,
            PartyCharacterSnapshot target,
            PartyInviteDisplayState display) => PartyOperationResult.Success();

        public PartyOperationResult AcceptInvite(
            PartyCharacterSnapshot invitee,
            PartyCharacterSnapshot inviter) => PartyOperationResult.Success();

        public PartyOperationResult TransferLeadership(
            CharacterId currentLeaderCharacterId,
            CharacterId nextLeaderCharacterId) => PartyOperationResult.Success();

        public PartyOperationResult RemoveMember(
            CharacterId currentLeaderCharacterId,
            CharacterId targetCharacterId) => PartyOperationResult.Success();

        public PartyOperationResult LeaveParty(CharacterId characterId) =>
            PartyOperationResult.Success();

        public PartyOperationResult CleanupCharacter(CharacterId characterId) =>
            PartyOperationResult.Success();

        public PartySnapshot FindParty(PartyCharacterSnapshot character) =>
            PartySnapshot.Virtual(character, new PartySettings(new byte[PartySettings.WireLength]));

    }

    private sealed class RecordingPositionPersistence : ISelectedSessionPositionPersistence
    {
        public int Writes { get; private set; }

        public CharacterPositionState? Position { get; private set; }

        public ValueTask PersistAsync(AccountId accountId, CharacterId characterId,
            Dfo.Server.Data.Characters.CharacterPositionState position,
            CancellationToken cancellationToken)
        {
            this.Writes++;
            this.Position = position;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDepartureSink : IDungeonRunParticipantDepartureSink
    {
        public List<(DungeonRunId RunId, CharacterId CharacterId)> Calls { get; } = [];

        public void ParticipantDeparted(DungeonRunId runId, CharacterId characterId) =>
            Calls.Add((runId, characterId));
    }
}
