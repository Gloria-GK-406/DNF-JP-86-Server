using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Trade;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using static Dfo.Server.Protocol.Game.Trade.TradeProjection;

namespace Dfo.Server.Protocol.Game.Trade;

internal sealed class GameSetTradeStateCommandHandler(IPlayerTradeDomainService trades,
    ISelectedSessionDomainService sessions, ICommandWorkUnits units, IGameCommandOutbox outbox,
    ICharacterInventoryEntryDomainService inventories, IAccountInventoryEntryDomainService accounts, TimeProvider clock)
    : ICommandHandler<GameSetTradeStateCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, GameSetTradeStateCommand command, CancellationToken ct) =>
        command.Input.Valid
            ? AdvanceAsync(context, command.Dispatch.Connection, command.Input.Metadata, command.Input.State, ct)
            : ValueTask.FromResult(false);

    private async ValueTask<bool> AdvanceAsync(CommandExecutionContext context, NetworkConnectionContext connection,
        GameMessageMetadata metadata, byte state, CancellationToken ct)
    {
        if (GameRequestFreshness.IsStale(metadata) || !sessions.TryGetInWorld(connection.ConnectionId, out var actor)
            || trades.Find(actor) is not { } trade) return false;
        var action = state switch
        {
            5 => TradeAction.Register,
            1 => TradeAction.Ready,
            3 => TradeAction.Confirm,
            _ => TradeAction.Cancel,
        };
        var advance = trades.Advance(trade, actor, action);
        switch (advance)
        {
            case TradeAdvance.Rejected:
                return false;
            case TradeAdvance.Canceled:
                await CloseAsync(trade, false, ct).ConfigureAwait(false);
                return true;
            case TradeAdvance.Settle:
                var settled = await SettleAsync(context, trade, actor, connection, metadata, ct).ConfigureAwait(false);
                await CloseAsync(trade, settled, ct).ConfigureAwait(false);
                return settled;
        }

        if (action != TradeAction.Confirm)
            foreach (var member in trade.Participants)
                await SendAsync(sessions, outbox, member, TradePackets.State(actor.WireUserId, state), ct).ConfigureAwait(false);
        if (advance == TradeAdvance.BothRegistered)
            foreach (var member in trade.Participants)
                await SendAsync(sessions, outbox, member, TradePackets.Registered(), ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> SettleAsync(CommandExecutionContext context, IPlayerTradeSession trade,
        SelectedSessionIdentity actor, NetworkConnectionContext connection, GameMessageMetadata metadata, CancellationToken ct)
    {
        try
        {
            return await units.WriteAsync(context, new("player-trade-settlement",
                Recipients(actor, trade.Opposite(actor), connection, metadata)), async (unit, token) =>
                {
                    try
                    {
                        if (await trades.SettleAsync(trade, token).ConfigureAwait(false)) return true;
                        unit.RequireRollback(new InvalidOperationException("Trade assets or participants changed."));
                        return false;
                    }
                    catch (ContainerAdditionException refusal) when (refusal.Reason == ContainerAdditionFailure.NoRoom)
                    {
                        unit.RequireRollback(refusal);
                        return false;
                    }
                }, ct).ConfigureAwait(false);
        }
        catch
        {
            trades.Invalidate(actor);
            throw;
        }
    }

    private async ValueTask CloseAsync(IPlayerTradeSession trade, bool settled, CancellationToken ct)
    {
        if (trades.Close(trade))
            await SendClosureAsync(sessions, outbox, inventories, accounts, clock, trade, settled, ct).ConfigureAwait(false);
    }
}
