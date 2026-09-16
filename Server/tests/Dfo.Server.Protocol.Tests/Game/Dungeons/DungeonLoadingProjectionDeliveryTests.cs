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

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

public sealed class DungeonLoadingProjectionDeliveryTests
{
    [Fact]
    public async Task The_exact_frozen_generation_submits_ready_for_its_current_room()
    {
        var world = World();

        var accepted = await world.Delivery.CompleteLoadingAsync(
            Current.ConnectionId, _ => ValueTask.CompletedTask, CancellationToken.None);

        Assert.True(accepted);
        Assert.Equal(1, world.Commands.Submissions);
        Assert.Contains(Current.CharacterId, world.Runs.Find(Current.CharacterId)!.ReadyParticipants);
    }

    [Fact]
    public async Task A_replacement_generation_cannot_inherit_the_frozen_runs_loading_submission()
    {
        var world = World();
        world.Sessions.Replace(Replacement);

        var accepted = await world.Delivery.CompleteLoadingAsync(
            Current.ConnectionId, _ => ValueTask.CompletedTask, CancellationToken.None);

        Assert.False(accepted);
        Assert.Equal(0, world.Commands.Submissions);
        Assert.Empty(world.Runs.Find(Current.CharacterId)!.ReadyParticipants);
    }

    [Fact]
    public async Task Replacement_waits_until_the_ready_command_and_result_validation_are_complete()
    {
        var enteredCommand = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommand = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var world = World(enteredCommand, releaseCommand);

        var loading = world.Delivery.CompleteLoadingAsync(
            Current.ConnectionId, _ => ValueTask.CompletedTask, CancellationToken.None).AsTask();
        await enteredCommand.Task;

        var replacementStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacement = Task.Run(async () =>
        {
            replacementStarted.SetResult();
            await using var lease = await world.TransitionGate.AcquireAsync(
                Current.AccountId, UserAccessMode.Exclusive, CancellationToken.None);
            world.Sessions.Replace(Replacement);
        });
        await replacementStarted.Task;
        await Task.Yield();
        Assert.False(replacement.IsCompleted);

        releaseCommand.SetResult();
        Assert.True(await loading);
        await replacement;
        Assert.Equal(Replacement, world.Sessions.FindByCharacter(Current.CharacterId));
    }

    [Fact]
    public async Task Replacement_also_waits_until_the_loading_complete_frame_is_delivered()
    {
        var world = World();
        var enteredDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loading = world.Delivery.CompleteLoadingAsync(
            Current.ConnectionId,
            async cancellationToken =>
            {
                enteredDelivery.SetResult();
                await releaseDelivery.Task.WaitAsync(cancellationToken);
            },
            CancellationToken.None).AsTask();
        await enteredDelivery.Task;

        var replacement = Task.Run(async () =>
        {
            await using var lease = await world.TransitionGate.AcquireAsync(
                Current.AccountId, UserAccessMode.Exclusive, CancellationToken.None);
            world.Sessions.Replace(Replacement);
        });
        await Task.Yield();
        Assert.False(replacement.IsCompleted);

        releaseDelivery.SetResult();
        Assert.True(await loading);
        await replacement;
        Assert.Equal(Replacement, world.Sessions.FindByCharacter(Current.CharacterId));
    }

    private static TestWorld World(
        TaskCompletionSource? enteredCommand = null,
        TaskCompletionSource? releaseCommand = null)
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
                new Dictionary<MazeCell, int> { [new MazeCell(0, 0)] = 61000 }),
            new MazeCell(0, 0),
            61000,
            new HashSet<MazeCell> { new(0, 0) },
            DateTimeOffset.UnixEpoch,
            RunId: DungeonRunId.New(),
            Participant: participant,
            Participants: [participant],
            EntryRevision: 1,
            CurrentCellState: DungeonCellSeed.CompleteEmpty(
                new MazeCell(0, 0), 61000).Snapshot());
        var sessions = new Sessions(Current);
        var runs = new Runs(run);
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, [Current]);
        var transitionGate = new SelectedSessionTransitionGate();
        var commands = new ReadyCommandBus(runs, enteredCommand, releaseCommand);
        var delivery = new DungeonLoadingProjectionDelivery(
            sessions,
            runs,
            registry,
            transitionGate,
            new DungeonProjectionGate(),
            commands);
        return new TestWorld(delivery, sessions, runs, commands, transitionGate);
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
        SelectedSessionTransitionGate TransitionGate);

    private sealed class Sessions(SelectedSessionIdentity initial) : ISelectedSessionDomainService
    {
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
        public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, byte difficulty) =>
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
