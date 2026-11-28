using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Trade;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Trade;
using static Dfo.Server.Protocol.Game.Trade.TradeProjection;

namespace Dfo.Server.Protocol.Game.Party;

internal sealed partial class GameRequestPeerCommandHandler
{
    private async ValueTask<bool> InviteTradeAsync(CommandExecutionContext context, NetworkConnectionContext connection,
        GameMessageMetadata metadata, ushort target, int peer, byte[] tail, CancellationToken ct)
    {
        if (GameRequestFreshness.IsStale(metadata) || !tail.All(value => value == 0)
            || !sessions.TryGetInWorld(connection.ConnectionId, out var actor)) return false;
        if (!sessions.TryGetInWorld(target, out var other) || other == actor)
        {
            await SendAsync(sessions, outbox, actor, new TradePeerReply(0x000A, [0, 3, TradePackets.PeerType]), ct).ConfigureAwait(false);
            return false;
        }

        var allowed = await units.ReadOnlyAsync(context, new("trade-invitation",
            Recipients(actor, other, connection, metadata)),
            token => trades.CanInviteAsync(actor, other, token), ct).ConfigureAwait(false);
        if (!allowed || trades.Invite(actor, other, peer) is not { } trade)
        {
            await SendAsync(sessions, outbox, actor, new TradePeerReply(0x000A, [0, 19, TradePackets.PeerType]), ct).ConfigureAwait(false);
            return false;
        }

        await SendAsync(sessions, outbox, actor, new TradePeerReply(0x000A, [1]), ct).ConfigureAwait(false);
        await SendAsync(sessions, outbox, other, TradePackets.Invitation(actor.WireUserId, trade.Peer), ct).ConfigureAwait(false);
        return true;
    }
}

internal sealed partial class GameResponsePeerCommandHandler
{
    private async ValueTask<bool> RespondTradeAsync(CommandExecutionContext context, NetworkConnectionContext connection,
        GameMessageMetadata metadata, ushort inviter, int peer, byte[] tail, CancellationToken ct)
    {
        if (GameRequestFreshness.IsStale(metadata) || !sessions.TryGetInWorld(connection.ConnectionId, out var actor)
            || trades.Find(actor) is not { Accepted: false } trade || trade.Invitee != actor
            || trade.Inviter.WireUserId != inviter) return false;
        var refused = tail.Length >= 2 && tail[0] == 0x85 && tail[1] == 0;
        if (!tail.Skip(refused ? 2 : 0).All(value => value == 0)) return false;
        if (refused || !trades.IsUsable(trade) || !await units.ReadOnlyAsync(context, new("trade-acceptance",
                Recipients(actor, trade.Inviter, connection, metadata)), token => trades.CanAcceptAsync(trade, token), ct)
                .ConfigureAwait(false) || !trades.Accept(trade))
        {
            if (trades.Close(trade))
                await SendAsync(sessions, outbox, trade.Inviter, TradePackets.Refused(), ct).ConfigureAwait(false);
            return false;
        }

        await SendAsync(sessions, outbox, actor, TradePackets.Accepted(inviter), ct).ConfigureAwait(false);
        await SendAsync(sessions, outbox, trade.Inviter, TradePackets.Open(actor.WireUserId, peer), ct).ConfigureAwait(false);
        return true;
    }
}
