using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;

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
        OnePayload(message, message.Success ? new byte[] {1, message.Mode, message.Selection} : new byte[] {0, 0});
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
public sealed class CrystalContractHandler(ICommandBus commands)
    : ClientMessageHandler<CrystalContractRequest>, IProtocolCommandMessageHandler<GameCrystalContractCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, CrystalContractRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameCrystalContractCommand(context, message), ct).ConfigureAwait(false);
}
