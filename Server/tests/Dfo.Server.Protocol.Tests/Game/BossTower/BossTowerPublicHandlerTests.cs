using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Tests.Support;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.BossTowerContinue;
using Dfo.Server.Protocol.Game.BossTowerReward;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.BossTower;

public sealed partial class BossTowerProjectionConcurrencyTests
{
    [Fact]
    public async Task Finish_loading_handler_waits_for_all_members_then_starts_once_and_opens_each_gate()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var first = Session(1); var second = Session(2);
            var initial = PartyRun(Guid.NewGuid(), first, second) with
            {
                Room = PartyRun(Guid.NewGuid(), first, second).Room with
                { Phase = TowerRoomPhase.Loading, LoadedMembers = ImmutableHashSet<Dfo.Server.Data.Characters.CharacterId>.Empty },
            };
            var towers = new TowerDomain(initial); var sender = new Sender();
            var commands = new FlowCommands(towers);
            var projection = Delivery(towers, new Sessions(first, second), commands, sender);
            var handler = new LoadingFlow(towers, new Sessions(first, second), commands, sender);

            await handler.HandleAsync(Dispatch(first), new FinishLoadingRequest(GameMessageMetadata.Empty, 0, 0), default);
            Assert.Empty(sender.Deliveries);
            await handler.HandleAsync(Dispatch(second), new FinishLoadingRequest(GameMessageMetadata.Empty, 0, 0), default);
            Assert.Equal(1, commands.Starts);
            Assert.Equal(TowerRoomPhase.Fighting, towers.Find(first)!.Room.Phase);
            Assert.Equal(2, sender.Deliveries.Count);
            Assert.All(sender.Deliveries, value => Assert.IsType<LoadingCompleteNotification>(Assert.Single(value.Delivery.RequiredDirectMessages)));
            await handler.HandleAsync(Dispatch(first), new FinishLoadingRequest(GameMessageMetadata.Empty, 0, 0), default);
            Assert.Equal(1, commands.Starts);
            Assert.Equal(2, sender.Deliveries.Count);
        });
    }

    [Fact]
    public async Task Result_hover_only_acknowledges_and_retry_waits_for_000F_before_a_new_0010_admission()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = Session(1); var towers = new TowerDomain(Run(Guid.NewGuid(), session, 3, TowerRoomPhase.Cleared));
            var sender = new Sender(); var commands = new FlowCommands(towers);
            var sessions = new Sessions(session); var projection = Delivery(towers, sessions, commands, sender);
            var handler = new EplpDriver(sessions, new NoViews(), null!, null!, null!, sender, null!,
                NullLogger<EplpCommandHandler>.Instance, bossTower: towers, choose: commands.ChooseAsync);

            await handler.HandleAsync(Dispatch(session), new EplpCommandRequest(GameMessageMetadata.Empty, 2, 0), default);
            Assert.Equal(0, commands.ResultChoices);
            Assert.Equal(3, commands.Tickets);
            Assert.Equal(new EplpCommandResponse(2, 0), Assert.Single(sender.Deliveries).Delivery.RequiredDirectMessages.Single());
            sender.Deliveries.Clear();
            await handler.HandleAsync(Dispatch(session), new EplpCommandRequest(GameMessageMetadata.Empty, 1, 0), default);
            Assert.Equal(1, commands.ResultChoices);
            Assert.Equal(3, commands.Tickets);
            Assert.NotNull(towers.Find(session));
            Assert.IsType<EplpCommandResponse>(Assert.Single(sender.Deliveries).Delivery.RequiredDirectMessages.Single());

            var select = Dfo.Server.Protocol.Tests.Game.DungeonEntry.DungeonSelectionTestDriver.Enter(null!, sessions, new NoViews(), null!, new Intents(), sender,
                null!, NullLogger<EnterSelectDungeonHandler>.Instance, null!, bossTower: towers);
            await select.HandleAsync(Dispatch(session), new EnterSelectDungeonRequest(GameMessageMetadata.Empty, 3505), default);
            Assert.Null(towers.Find(session));
            Assert.Equal(3, commands.Tickets);
            // The only new charge is the later admission, rather than the button or old-run release.
            await commands.SendAsync(new Dfo.Server.Protocol.Tests.Game.DungeonEntry.BossEntryRouteProbe([new(session, 0)], session, 17, 0, 0, 0));
            Assert.Equal(2, commands.Tickets);
        });
    }

    [Fact]
    public async Task Invalid_move_is_handled_without_ordinary_fallback_and_older_selected_session_cannot_start()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = Session(1); var current = PartyRun(Guid.NewGuid(), session);
            var towers = new TowerDomain(current); var sessions = new Sessions(session); var sender = new Sender();
            var commands = new FlowCommands(towers); var projection = Delivery(towers, sessions, commands, sender);
            var move = new LoadingFlow(towers, sessions, commands, sender);
            await move.HandleAsync(Dispatch(session), new MoveMapRequest(GameMessageMetadata.Empty, 9, 9, 0, 0), default);
            Assert.Empty(sender.Deliveries);
            Assert.Equal(1, towers.Find(session)!.CurrentStage);
            var replacement = session with { ConnectionId = new("replacement"), ConnectionGeneration = 2 };
            sessions.Replace(replacement);
            Assert.False(await move.CompleteLoadingAsync(replacement.ConnectionId, GameMessageMetadata.Empty, default));
            Assert.Equal(0, commands.Starts);
        });
    }

    [Fact]
    public async Task Settlement_rows_use_each_receivers_own_experience_and_never_repeat_on_a_second_death()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var first = Session(1); var second = Session(2);
            var receipt = new TowerSettlementReceipt([
                new(first.CharacterId, 11, 0, []) { Session = first, Slot = 0, Candidates = [new(0, 7)] },
                new(second.CharacterId, 22, 0, []) { Session = second, Slot = 1, Candidates = [new(-1, 0)] },
            ]);
            var run = PartyRun(Guid.NewGuid(), first, second) with
            { Completion = new(TowerEndReason.Defeated, 0, 1, true, Now, receipt) };
            var towers = new TowerDomain(run); var sender = new Sender();
            var projection = new Dfo.Server.Protocol.Tests.Game.CharacterDeath.DeathProjectionDriver(new Sessions(first,second),sender,towers,null);
            Assert.Empty(await projection.BossAsync(new(true,run){SettlementCreated=true},first,default));
            Assert.Equal(2, sender.Deliveries.Count);
            foreach (var delivery in sender.Deliveries)
            {
                var reward = Assert.Single(delivery.Delivery.RequiredDirectMessages.OfType<BossTowerRewardNotification>());
                Assert.Equal(delivery.Connection == first.ConnectionId ? 11u : 22u, reward.Experience);
                Assert.Equal(2, reward.Slots.Count);
                Assert.IsType<BossTowerContinueNotification>(delivery.Delivery.RequiredDirectMessages.Last());
            }
        });
    }

    private static NetworkDispatchContext Dispatch(SelectedSessionIdentity session) =>
        new(new NetworkConnectionContext(session.ConnectionId, NetworkEndpointKind.Game, 10011));

    // Same production phase body; fake domain actions below intentionally do not prove bus/scope behavior.
    private sealed class LoadingFlow(TowerDomain towers, Sessions selected, FlowCommands commands, Sender sender)
    {
        private readonly GameMapLoadingCommandHandler handler = ProtocolAlgorithmFixture.Create<GameMapLoadingCommandHandler>(
            selected, LoadingDomain.Create(towers, commands), new Dfo.Server.Protocol.Tests.Game.DungeonEntry.LoadingOutbox(sender));
        public async ValueTask HandleAsync(NetworkDispatchContext dispatch, FinishLoadingRequest input, CancellationToken token)
        {
            var command = new GameFinishLoadingCommand(dispatch, input);
            _ = await handler.HandleAsync(ComponentContext.For(command, handler.GetType()), command, token);
        }
        public async ValueTask HandleAsync(NetworkDispatchContext dispatch, MoveMapRequest input, CancellationToken token)
        {
            var command = new GameMoveMapCommand(dispatch, input);
            _ = await handler.HandleAsync(ComponentContext.For(command, handler.GetType()), command, token);
        }
        public ValueTask<bool> CompleteLoadingAsync(NetworkConnectionId id, GameMessageMetadata metadata, CancellationToken token)
        {
            var command = new GameFinishLoadingCommand(new(new(id, NetworkEndpointKind.Game, 10011)), new(metadata, 0, 0));
            return handler.CompleteBossLoadingAsync(ComponentContext.For(command, handler.GetType()), id, metadata, token);
        }
    }
    private class LoadingDomain : System.Reflection.DispatchProxy
    {
        private TowerDomain towers = null!;
        private FlowCommands commands = null!;
        internal static IBossTowerDomainService Create(TowerDomain towers, FlowCommands commands)
        {
            var result = Create<IBossTowerDomainService, LoadingDomain>();
            ((LoadingDomain)result).towers = towers;
            ((LoadingDomain)result).commands = commands;
            return result;
        }
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            if (method is null || args is null) throw new NotSupportedException();
            if (method.Name == nameof(IBossTowerDomainService.LoadAsync))
                return ValueTask.FromResult(commands.Load((BossTowerActionContext)args[0]!));
            if (method.Name == nameof(IBossTowerDomainService.StartAsync))
                return commands.Start((BossTowerActionContext)args[0]!);
            if (method.Name == nameof(IBossTowerDomainService.NextLevelAsync))
                return ValueTask.FromResult(new BossTowerActionResult(false, towers.Find(((BossTowerActionContext)args[0]!).Session)));
            try { return method.Invoke(towers, args); }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
    }
    private sealed class FlowCommands(TowerDomain towers) : ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public BossTowerActionResult Load(Dfo.Server.Abstractions.World.BossTower.BossTowerActionContext context)
        {
            var loading = towers.Find(context.Session)!;
            if (loading.Room.Phase != TowerRoomPhase.Loading) return new(false, loading);
            var loaded = loading.Room.LoadedMembers.Add(context.Session.CharacterId);
            loading = loading with { Room = loading.Room with { LoadedMembers = loaded,
                Phase = loading.Members.All(value => loaded.Contains(value.Session.CharacterId)) ? TowerRoomPhase.Ready : TowerRoomPhase.Loading } };
            towers.Put(loading); return new(true, loading);
        }
        public ValueTask<BossTowerActionResult> ChooseAsync(BossTowerActionContext context, byte option, CancellationToken ct)
        {
            ResultChoices++; var chosen = towers.Find(context.Session)! with { PendingResultChoice = option };
            towers.Put(chosen); return ValueTask.FromResult(new BossTowerActionResult(true, chosen));
        }
        public ValueTask<BossTowerActionResult> Start(BossTowerActionContext context)
        {
            Starts++; var started = towers.Find(context.Session)!;
            started = started with { Room = started.Room with { Phase = TowerRoomPhase.Fighting } }; towers.Put(started);
            return ValueTask.FromResult(new BossTowerActionResult(true, started));
        }
        public int Starts { get; private set; }
        public int ResultChoices { get; private set; }
        public int Tickets { get; private set; } = 3;
        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) where TResult : notnull
        {
            object result;
            switch (command)
            {
                case Dfo.Server.Protocol.Tests.Game.DungeonEntry.BossEntryRouteProbe entry:
                    Tickets--; var entered = PartyRun(Guid.NewGuid(), entry.Leader); towers.Put(entered);
                    result = new BossTowerEntryResult(null, entered, []); break;

                default: throw new NotSupportedException(command.GetType().Name);
            }
            return ValueTask.FromResult((TResult)result);
        }
    }
}
