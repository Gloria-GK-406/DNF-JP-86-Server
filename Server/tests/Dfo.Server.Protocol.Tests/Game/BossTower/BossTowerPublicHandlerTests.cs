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
            var handler = new FinishLoadingHandler(sender, NullLogger<FinishLoadingHandler>.Instance, null!, bossTower: projection);

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
            var handler = new EplpCommandHandler(sessions, new NoViews(), null!, null!, null!, sender, null!,
                NullLogger<EplpCommandHandler>.Instance, bossTower: projection);

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

            var select = new EnterSelectDungeonHandler(null!, sessions, new NoViews(), null!, new Intents(), sender,
                null!, NullLogger<EnterSelectDungeonHandler>.Instance, null!, bossTower: projection);
            await select.HandleAsync(Dispatch(session), new EnterSelectDungeonRequest(GameMessageMetadata.Empty, 3505), default);
            Assert.Null(towers.Find(session));
            Assert.Equal(3, commands.Tickets);
            // The only new charge is the later admission, rather than the button or old-run release.
            await commands.SendAsync(new EnterBossTowerCommand([new(session, 0)], session, 17, 0, 0, 0));
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
            var move = new MoveMapHandler(sessions, null!, null!, null!, sender,
                NullLogger<MoveMapHandler>.Instance, bossTower: projection);
            await move.HandleAsync(Dispatch(session), new MoveMapRequest(GameMessageMetadata.Empty, 9, 9, 0, 0), default);
            Assert.Empty(sender.Deliveries);
            Assert.Equal(1, towers.Find(session)!.CurrentStage);
            var replacement = session with { ConnectionId = new("replacement"), ConnectionGeneration = 2 };
            sessions.Replace(replacement);
            Assert.False(await projection.CompleteLoadingAsync(replacement.ConnectionId, GameMessageMetadata.Empty, default));
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
            var projection = Delivery(towers, new Sessions(first, second), new ResultCommands(new(true, run) { SettlementCreated = true }), sender);
            await projection.DieAsync(first.ConnectionId, GameMessageMetadata.Empty, default);
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

    private sealed class FlowCommands(TowerDomain towers) : ICommandBus
    {
        public int Starts { get; private set; }
        public int ResultChoices { get; private set; }
        public int Tickets { get; private set; } = 3;
        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) where TResult : notnull
        {
            object result;
            switch (command)
            {
                case LoadBossTowerCommand load:
                    var loading = towers.Find(load.Context.Session)!;
                    var loaded = loading.Room.LoadedMembers.Add(load.Context.Session.CharacterId);
                    var room = loading.Room with { LoadedMembers = loaded,
                        Phase = loading.Members.All(value => loaded.Contains(value.Session.CharacterId)) ? TowerRoomPhase.Ready : TowerRoomPhase.Loading };
                    if (loading.Room.Phase != TowerRoomPhase.Loading) { result = new BossTowerActionResult(false, loading); break; }
                    loading = loading with { Room = room }; towers.Put(loading);
                    result = new BossTowerActionResult(true, loading); break;
                case StartBossTowerCommand start:
                    Starts++; var started = towers.Find(start.Context.Session)!;
                    started = started with { Room = started.Room with { Phase = TowerRoomPhase.Fighting } }; towers.Put(started);
                    result = new BossTowerActionResult(true, started); break;
                case SelectBossTowerResultCommand choose:
                    ResultChoices++; var chosen = towers.Find(choose.Context.Session)! with { PendingResultChoice = choose.Option };
                    towers.Put(chosen); result = new BossTowerActionResult(true, chosen); break;
                case ReleaseBossTowerSelectionCommand release:
                    result = towers.Leave(release.Context.Session); break;
                case EnterBossTowerCommand entry:
                    Tickets--; var entered = PartyRun(Guid.NewGuid(), entry.Leader); towers.Put(entered);
                    result = new BossTowerEntryResult(null, entered, []); break;
                case AdvanceBossTowerCommand advance:
                    result = new BossTowerActionResult(false, towers.Find(advance.Context.Session)); break;
                default: throw new NotSupportedException(command.GetType().Name);
            }
            return ValueTask.FromResult((TResult)result);
        }
    }
}
