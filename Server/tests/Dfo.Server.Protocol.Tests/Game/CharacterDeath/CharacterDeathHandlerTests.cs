using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;

using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.CharacterDeath;

public sealed class CharacterDeathHandlerTests
{
    private static readonly SelectedSessionIdentity Current = SelectedSessionIdentity.Create(
        new AccountId(1), new CharacterId(1001), new NetworkConnectionId("current"), 1, 10011);
    private static readonly SelectedSessionIdentity Other = SelectedSessionIdentity.Create(
        new AccountId(2), new CharacterId(1002), new NetworkConnectionId("other"), 2, 10011);

    [Theory]
    [InlineData(false, false, 1, 1)]
    [InlineData(true, false, 0, 0)]
    [InlineData(false, true, 1, 0)]
    public async Task Death_targets_exact_admitted_sessions_and_duplicate_reports_do_not_restart_the_ui(
        bool replaceActor, bool replaceMember, int directCount, int secondaryCount)
    {
        var participant = new DungeonRunParticipant(Current.AccountId, Current.CharacterId, 0, true);
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
                new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(61000)}),
            new MazeCell(0, 0),
            61000,
            new HashSet<MazeCell> { new(0, 0) },
            DateTimeOffset.UnixEpoch,
            RunId: DungeonRunId.New(),
            Participant: participant,
            Participants: [participant, new DungeonRunParticipant(Other.AccountId, Other.CharacterId, 1, false)],
            EntryRevision: 1,
            CurrentCellState: DungeonCellSeed.CompleteEmpty(
                new MazeCell(0, 0), 61000).Snapshot());
        var runs = new Runs(run);
        var sessions = new Sessions();
        if (replaceActor) sessions.Values[Current.CharacterId] = Current with { ConnectionGeneration = 2 };
        if (replaceMember) sessions.Values[Other.CharacterId] = Other with { ConnectionGeneration = Other.ConnectionGeneration + 1 };
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, [Current, Other]);
        var sender = new Sender();
        var handler = new CharacterDeathHandler(sessions, runs, registry,
            new SelectedSessionTransitionGate(), new DungeonProjectionGate(), new Commands(runs),
            sender, NullLogger<CharacterDeathHandler>.Instance);
        var context = new NetworkDispatchContext(new NetworkConnectionContext(Current.ConnectionId, NetworkEndpointKind.Game, 10011));
        var request = new CharacterDeathRequest(GameMessageMetadata.Empty, 612, 294, 0);
        await handler.HandleAsync(context, request, CancellationToken.None);
        await handler.HandleAsync(context, request, CancellationToken.None);
        Assert.Equal(directCount, sender.Deliveries.Count);
        if (directCount == 0) { Assert.Empty(runs.Find(Current.CharacterId)!.DeadParticipants); return; }
        var delivery = Assert.Single(sender.Deliveries);
        Assert.Equal(2, delivery.RequiredDirectMessages.Count);
        Assert.IsType<CharacterDeathResponse>(delivery.RequiredDirectMessages[0]);
        var noti = Assert.IsType<CharacterLifeStateNotification>(delivery.RequiredDirectMessages[1]);
        Assert.Equal(Current.WireUserId, noti.ActorId);
        Assert.Equal(secondaryCount, delivery.BestEffortTargets.Count);
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

        public Dictionary<CharacterId, SelectedSessionIdentity> Values { get; } = new() { [Current.CharacterId] = Current, [Other.CharacterId] = Other };
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => Values.Values.SingleOrDefault(x => x.ConnectionId == connectionId);
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Values.GetValueOrDefault(characterId);
        public SelectedSessionIdentity? Find(AccountId accountId) => Values.Values.SingleOrDefault(x => x.AccountId == accountId);
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Values.Values.SingleOrDefault(x => x.WireUserId == wireUserId);
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }
    private sealed class Commands(Runs runs) : ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) where TResult : notnull
        {
            var death = Assert.IsType<MarkDungeonParticipantDeadCommand>(command);
            return ValueTask.FromResult((TResult)(object)new MarkDungeonParticipantDeadResult(
                runs.TryMarkParticipantDead(death.CharacterId, death.RunId, death.RoomVersion)));
        }
    }
    private sealed class Sender : IGameMessageSender
    {
        public List<GameExplicitMessageDelivery> Deliveries { get; } = [];
        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SendExplicitAsync(NetworkConnectionId connectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken)
        { Assert.Equal(Current.ConnectionId, connectionId); Deliveries.Add(delivery); return ValueTask.CompletedTask; }
    }
    private sealed class Runs(DungeonRunState initial) : IDungeonRunDomainService
    {
        private DungeonRunState current = initial;

        public DungeonRunState? TryMarkParticipantDead(CharacterId characterId, DungeonRunId runId, long version)
        {
            if (current.RunId != runId || current.RoomVersion != version || current.DeadParticipants.Contains(characterId)) return null;
            current = current with { DeadParticipants = current.DeadParticipants.Add(characterId) };
            return current;
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

}
