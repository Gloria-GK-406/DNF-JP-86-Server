using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

public sealed class DungeonLoadingProjectionDeliveryTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(28)]
    [InlineData(100)]
    public async Task Entry_restores_retained_weakness_after_loading_without_reapplying_on_duplicate(byte value)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = World(weaknessValue: value);
            var released = false;
            world.Sender.BeforeSend = () => Assert.True(released);
            Assert.True(await world.Delivery.CompleteLoadingAsync(Current.ConnectionId,
                _ => { released = true; return ValueTask.CompletedTask; }, default));
            Assert.Equal(value, Assert.Single(world.Sender.Values));
            Assert.True(await world.Delivery.CompleteLoadingAsync(Current.ConnectionId,
                _ => ValueTask.CompletedTask, default));
            Assert.Single(world.Sender.Values);
        });
    }

    [Fact]
    public async Task Later_room_loading_does_not_reapply_weakness_and_reset_combat_vitals()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = World(roomVersion: 1);
            Assert.True(await world.Delivery.CompleteLoadingAsync(Current.ConnectionId,
                _ => ValueTask.CompletedTask, default));
            Assert.Empty(world.Sender.Values);
        });
    }

    [Fact]
    public async Task The_exact_frozen_generation_submits_ready_for_its_current_room()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = World();

            var accepted = await world.Delivery.CompleteLoadingAsync(
                Current.ConnectionId, _ => ValueTask.CompletedTask, CancellationToken.None);

            Assert.True(accepted);
            Assert.Equal(1, world.Commands.Submissions);
            Assert.Contains(Current.CharacterId, world.Runs.Find(Current.CharacterId)!.ReadyParticipants);
        });
    }

    [Fact]
    public async Task A_replacement_generation_cannot_inherit_the_frozen_runs_loading_submission()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = World();
            world.Sessions.Replace(Replacement);

            var accepted = await world.Delivery.CompleteLoadingAsync(
                Current.ConnectionId, _ => ValueTask.CompletedTask, CancellationToken.None);

            Assert.False(accepted);
            Assert.Equal(0, world.Commands.Submissions);
            Assert.Empty(world.Runs.Find(Current.CharacterId)!.ReadyParticipants);
            Assert.Empty(world.Sender.Values);
        });
    }

    [Fact]
    public async Task Mirror_authority_is_projected_only_after_loading_release_without_adding_a_CMD_ACK_or_deducting_again()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var events = new QuotaEvents();
            var world = World(mirror: true, quotaProjection: new(events));
            var released = false;
            events.BeforeProjection = () => Assert.True(released);
            Assert.True(await world.Delivery.CompleteLoadingAsync(Current.ConnectionId,
                _ => { released = true; return ValueTask.CompletedTask; }, default));
            Assert.Equal(Current, Assert.Single(Assert.IsType<NotificationAudience.TheseSessions>(events.Audiences.Single()).Sessions));
            Assert.Equal(1, world.Commands.Submissions);
            Assert.True(await world.Delivery.CompleteLoadingAsync(Current.ConnectionId, _ => ValueTask.CompletedTask, default));
            Assert.Equal(2, events.Audiences.Count); // A repeated load may reconcile; it never records another entry.
            var ordinary = World(quotaProjection: new(events));
            Assert.True(await ordinary.Delivery.CompleteLoadingAsync(Current.ConnectionId, _ => ValueTask.CompletedTask, default));
            Assert.Equal(2, events.Audiences.Count);
        });
    }

    private static TestWorld World(
        TaskCompletionSource? enteredCommand = null,
        TaskCompletionSource? releaseCommand = null,
        byte weaknessValue = 19,
        long roomVersion = 0,
        bool mirror = false,
        UsageQuotaProjectionDelivery? quotaProjection = null)
    {
        var participant = new DungeonRunParticipant(
            Current.AccountId, Current.CharacterId, Slot: 0, IsLeader: true);
        var run = new DungeonRunState(
            new DungeonLayout(
                10000,
                0,
                1,
                1,
                new MazeCell(0, 0),
                new MazeCell(0, 0),
                61000,
                1,
                new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(61000)})
            { Mirror = mirror ? new(8000, 31, 14299) : null },
            new MazeCell(0, 0),
            61000,
            new HashSet<MazeCell> { new(0, 0) },
            DateTimeOffset.UnixEpoch,
            RunId: DungeonRunId.New(),
            RoomVersion: roomVersion,
            Participant: participant,
            Participants: [participant],
            EntryRevision: 1,
            CurrentCellState: DungeonCellSeed.CompleteEmpty(
                new MazeCell(0, 0), 61000).Snapshot());
        var sessions = new Sessions(Current);
        var runs = new Runs(run);
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, [Current]);
        var commands = new ReadyCommandBus(runs, enteredCommand, releaseCommand);
        var sender = new WeaknessSender();
        var delivery = new DungeonLoadingProjectionDelivery(
            sessions,
            runs,
            registry,
            commands,
            new WeaknessProjectionDelivery(new WeaknessState(weaknessValue), sessions, sender), quotaProjection);
        return new TestWorld(delivery, sessions, runs, commands, sender);
    }

    private static readonly SelectedSessionIdentity Current = SelectedSessionIdentity.Create(
        new AccountId(1), new CharacterId(1001), new NetworkConnectionId("current"), 1, 10011);

    private static readonly SelectedSessionIdentity Replacement = Current with
    {
        ConnectionId = new NetworkConnectionId("replacement"),
        ConnectionGeneration = 2,
    };

    private sealed record TestWorld(
        DungeonLoadingProjectionDelivery Delivery,
        Sessions Sessions,
        Runs Runs,
        ReadyCommandBus Commands,
        WeaknessSender Sender);

    private sealed class WeaknessState(byte value) : IWeaknessDomainService, IWeakness
    {
        public byte Value => value;
        public ValueTask<int> RecoveryGoldCost(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IWeakness For(CharacterId id) => this;
        public TimeSpan RecoveryInterval => TimeSpan.FromMinutes(1);
        public IReadOnlyList<CharacterId> AdvanceRecovery() => [];
        public ValueTask SetWeakness() => throw new NotSupportedException();
        public void Recover() => throw new NotSupportedException();
        public void PauseRecovery() => throw new NotSupportedException();
        public void ResumeRecovery() => throw new NotSupportedException();
    }

    /// <summary>
    /// The weakness values a real registration adapter registered, read from its outbox; every call goes to the real
    /// protocol sender over it, after the recipient check and the callback at the registration point (TASK-25). A CMD or
    /// an explicit delivery fails there as the old double refused one.
    /// </summary>
    private sealed class WeaknessSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();
        public WeaknessSender()
        {
            outbound.RefuseExplicit();
            outbound.FailAny(message => outbound.ExplicitInFlight is null && message is IGameCmdMessage, () => new NotSupportedException());
        }
        public List<byte> Values => [.. outbound.Messages.Select(message => Assert.IsType<WeaknessProjectionNotification>(message).Value)];
        public Action? BeforeSend { get; set; }
        public ValueTask SendNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken token)
        {
            Assert.Equal(Current.ConnectionId, id);
            BeforeSend?.Invoke();
            return outbound.Game.SendNotiAsync(id, message, token);
        }
        public ValueTask SendCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken token) =>
            outbound.Game.SendCmdAsync(id, message, token);
        public ValueTask SendExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken token) =>
            outbound.Game.SendExplicitAsync(id, delivery, token);
    }

    private sealed class Sessions(SelectedSessionIdentity initial) : ISelectedSessionDomainService
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

        private readonly Dictionary<NetworkConnectionId, SelectedSessionIdentity> byConnection =
            new() { [initial.ConnectionId] = initial };
        private SelectedSessionIdentity current = initial;

        public void Replace(SelectedSessionIdentity replacement)
        {
            current = replacement;
            byConnection[replacement.ConnectionId] = replacement;
        }

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            byConnection.GetValueOrDefault(connectionId);
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            current.CharacterId == characterId ? current : null;
        public SelectedSessionIdentity? Find(AccountId accountId) =>
            current.AccountId == accountId ? current : null;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
            current.WireUserId == wireUserId ? current : null;
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) =>
            throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException();
    }

    private sealed class Runs(DungeonRunState initial) : IDungeonRunDomainService
    {
        private DungeonRunState current = initial;

        public DungeonEntryChange? MarkReady(
            CharacterId characterId,
            DungeonRunId runId,
            long roomVersion)
        {
            if (current.RunId != runId
                || current.RoomVersion != roomVersion
                || current.ReadyParticipants.Contains(characterId))
            {
                return null;
            }

            current = current with
            {
                EntryRevision = current.EntryRevision + 1,
                ReadyParticipants = current.ReadyParticipants.Append(characterId).ToHashSet(),
            };
            return new DungeonEntryChange(
                DungeonEntryChangeKind.ParticipantReady,
                current,
                current.EntryRevision,
                [characterId]);
        }

        public DungeonRunState? Find(CharacterId characterId) =>
            current.Participants.Any(value => value.CharacterId == characterId) ? current : null;
        public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, DungeonDifficulty difficulty) =>
            throw new NotSupportedException();
        public DungeonRunState? TryMove(CharacterId characterId, MazeCell target) => null;
        public DungeonRunState? TryTakeGroundDrop(CharacterId characterId, ushort groundDropNumber) => null;
        public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence) => null;
        public DungeonRunState? TrySettle(CharacterId characterId) => null;
        public IReadOnlyList<MonsterDrop> AllocateMonsterDrops(
            CharacterId characterId,
            int declaredDropCount,
            ushort actorSequence,
            IReadOnlyList<RolledDrop> rolled) => [];
        public DungeonRunState? FixClearRewardCards(
            CharacterId characterId,
            ClearRewardCardFace free,
            ClearRewardCardFace paid,
            int? paidFlipCost) => null;
        public ClearRewardCardSlot? TryClaimClearRewardCard(
            CharacterId characterId,
            ClearRewardCardKind kind,
            int index) => null;
        public bool End(CharacterId characterId) => false;
    }

    private sealed class QuotaEvents : IEventService
    {
        public List<NotificationAudience> Audiences = [];
        public Action? BeforeProjection;
        public ValueTask<Guid> EnqueueAsync<T>(T payload, CancellationToken cancellationToken = default) where T : class, IDomainEvent => throw new NotSupportedException();
        public ValueTask DirectAsync<T>(T payload, CancellationToken cancellationToken = default) where T : class, IDomainEvent => throw new NotSupportedException();
        public ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience, T notification, CancellationToken cancellationToken = default)
            where T : class, INotification<T>
        {
            BeforeProjection?.Invoke();
            Assert.Equal(new UsageQuotaViewChanged(Current.CharacterId, UsageQuotaFamily.MirrorDungeon), Assert.IsType<UsageQuotaViewChanged>(notification));
            Audiences.Add(audience);
            return ValueTask.FromResult(new NotificationDelivery([new(Current.CharacterId, NotificationOutcome.Delivered)]));
        }
    }

    private sealed class ReadyCommandBus(
        Runs runs,
        TaskCompletionSource? entered,
        TaskCompletionSource? release) : ICommandBus
    {
        public int Submissions { get; private set; }

        public async ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            var ready = Assert.IsType<MarkDungeonParticipantReadyCommand>(command);
            Submissions++;
            entered?.SetResult();
            if (release is not null)
                await release.Task.WaitAsync(cancellationToken);
            object result = new MarkDungeonParticipantReadyResult(
                runs.MarkReady(ready.CharacterId, ready.RunId, ready.RoomVersion));
            return (TResult)result;
        }
    }
}
