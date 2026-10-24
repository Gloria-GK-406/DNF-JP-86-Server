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
public sealed record ClaimDailyReviveCoinCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session)
    : ICommand<DailyReviveCoinResult>, IAccountCommand
{ public AccountId AccountId => Session.AccountId; }
public sealed class ClaimDailyReviveCoinCommandHandler(IReviveCoinDomainService coins)
    : ICommandHandler<ClaimDailyReviveCoinCommand, DailyReviveCoinResult>
{
    public ValueTask<DailyReviveCoinResult> HandleAsync(CommandExecutionContext context, ClaimDailyReviveCoinCommand command, CancellationToken ct) =>
        coins.ClaimAsync(command.Session, context.LegacyFootprint, ct);
}
internal sealed class DailyReviveCoinHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ICommandBus commands, IEventService events, IGameMessageSender sender, ILogger<DailyReviveCoinHandler> logger)
    : ClientMessageHandler<DailyReviveCoinRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, DailyReviveCoinRequest message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        var result = new DailyReviveCoinResult(false, false);
        if (session is not null && users.TryGetByConnection(session.ConnectionId, out var online) && online is not null)
            result = await commands.SendAsync(new ClaimDailyReviveCoinCommand(online.OnlineUserId, session), ct).ConfigureAwait(false);
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
            await sender.SendExplicitAsync(context.Connection.ConnectionId, new(replies, []), ct).ConfigureAwait(false);
        }
    }
}
