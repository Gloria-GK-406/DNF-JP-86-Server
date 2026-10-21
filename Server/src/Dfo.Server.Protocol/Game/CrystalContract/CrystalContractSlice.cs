using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CrystalContract;

public sealed record CrystalContractRequest(GameMessageMetadata Metadata, byte Mode, byte Selection) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameCrystalContract;
}
internal sealed class CrystalContractDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 2) throw new InvalidDataException("Crystal contract selection requires two bytes.");
        return new CrystalContractRequest(metadata, body[0], body[1]);
    }
}
public sealed record CrystalContractResponse(bool Success, byte Mode, byte Selection) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCrystalContract;
}
internal sealed class CrystalContractEncoder : GameCmdMessageEncoder<CrystalContractResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CrystalContractResponse message) =>
        OnePayload(0x0218, message.Success ? new byte[] {1, message.Mode, message.Selection} : new byte[] {0, 0});
}
public sealed record SelectCrystalContractCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    byte Mode, byte Selection) : ICommand<bool>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class SelectCrystalContractCommandHandler(ICrystalContractDomainService crystals)
    : ICommandHandler<SelectCrystalContractCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, SelectCrystalContractCommand command, CancellationToken ct) =>
        crystals.SelectAsync(command.Session, command.Mode, command.Selection, ct);
}
public sealed record CrystalContractSelected(SelectedSessionIdentity Session, byte Mode, byte Selection)
    : INotification<CrystalContractSelected>;
public sealed class CrystalContractSelectedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<CrystalContractSelected>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        CrystalContractSelected message, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            recipient.CharacterId == message.Session.CharacterId && recipient.ConnectionId == message.Session.ConnectionId
            && sessions.IsInWorld(message.Session)
            ? [new CrystalContractResponse(true, message.Mode, message.Selection),
                new CubeProgressionStateMessage(message.Mode, message.Selection)] : []);
}
public sealed class CrystalContractHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ICommandBus commands, IEventService notifications, IGameMessageSender sender, ILogger<CrystalContractHandler> logger)
    : ClientMessageHandler<CrystalContractRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, CrystalContractRequest message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user is null) return;
        var saved = await commands.SendAsync(new SelectCrystalContractCommand(user.OnlineUserId, session, message.Mode, message.Selection), ct);
        logger.LogInformation("Crystal contract selection character={CharacterId} mode={Mode} selection={Selection} saved={Saved}",
            session.CharacterId.Value, message.Mode, message.Selection, saved);
        if (!sessions.IsInWorld(session)) return;
        if (saved)
            await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
                new CrystalContractSelected(session, message.Mode, message.Selection), ct);
        else
            await sender.SendCmdAsync(context.Connection.ConnectionId, new CrystalContractResponse(false, 0, 255), ct);
    }
}
