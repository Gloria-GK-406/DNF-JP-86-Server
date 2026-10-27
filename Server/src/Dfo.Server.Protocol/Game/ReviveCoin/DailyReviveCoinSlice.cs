using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.ReviveCoin;

public sealed record DailyReviveCoinRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameDailyReviveCoin; }
internal sealed class DailyReviveCoinDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => body.IsEmpty
        ? new DailyReviveCoinRequest(metadata) : throw new InvalidDataException("SHOP_COIN_EVENT has an empty body.");
}
public sealed record DailyReviveCoinResponse(bool Granted) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameDailyReviveCoin; }
internal sealed class DailyReviveCoinEncoder : GameCmdMessageEncoder<DailyReviveCoinResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DailyReviveCoinResponse message)
    {
        // A21 VA 01127277 reads one u8 grant count after the CMD success selector.
        byte[] body = message.Granted ? [1, 1] : [0];
        return OnePayload(0x00CF, body);
    }
}
public sealed record GameDailyReviveCoinCommand(NetworkDispatchContext Dispatch, DailyReviveCoinRequest Input)
    : IProtocolCommand<DailyReviveCoinResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameDailyReviveCoinCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    IReviveCoinDomainService coins, ICommandWorkUnits units, ICommandProjections events, IGameCommandOutbox outbox,
    ILogger<GameDailyReviveCoinCommandHandler> logger) : ICommandHandler<GameDailyReviveCoinCommand, DailyReviveCoinResult>
{
    public async ValueTask<DailyReviveCoinResult> HandleAsync(CommandExecutionContext context,
        GameDailyReviveCoinCommand command, CancellationToken ct)
    {
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        var result = new DailyReviveCoinResult(false, false);
        if (session is not null && users.TryGetByConnection(session.ConnectionId, out var online) && online is not null)
            result = await units.WriteAsync(context, new("DailyReviveCoin", [],
                    Footprint: CommandFootprintMode.DeclaredNonRequest, FootprintOwners: [session]),
                (unit, token) => coins.ClaimAsync(session, unit.Footprint, token), ct).ConfigureAwait(false);
        logger.LogInformation("Daily revive coin character={Character} granted={Granted} claimed={Claimed}", session?.CharacterId, result.Granted, result.Claimed);
        if (result.Receipt is { } receipt)
            await events.ProjectAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new OpeningCompleted(receipt, [new DailyReviveCoinResponse(true)], FollowingMessages: [new DailyReviveCoinStateMessage(true)]), ct).ConfigureAwait(false);
        else
        {
            List<IGameServerMessage> replies = [new DailyReviveCoinResponse(false)];
            if (result.Wallet is { } wallet && result.PreparedAt is { } at)
                replies.Add(GameInventoryDelivery.Notification(new(InventoryContainerType.Main, [new(null, 1, wallet)], at)));
            replies.Add(new DailyReviveCoinStateMessage(result.Claimed));
            await outbox.RegisterExplicitAsync(command.Dispatch.Connection.ConnectionId, new(replies, []), ct).ConfigureAwait(false);
        }
        return result;
    }
}

internal sealed class DailyReviveCoinHandler(ICommandBus commands)
    : ClientMessageHandler<DailyReviveCoinRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameDailyReviveCoinCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, DailyReviveCoinRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameDailyReviveCoinCommand(context, message), ct).ConfigureAwait(false);
}
