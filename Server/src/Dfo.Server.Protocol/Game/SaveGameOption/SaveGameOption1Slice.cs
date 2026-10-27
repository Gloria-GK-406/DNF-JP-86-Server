using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SaveGameOption;

public sealed record SaveGameOption1Request(GameMessageMetadata Metadata, byte[] Options) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSaveGameOption1;
}
internal static class SaveGameOption1PayloadCodec
{
    public static SaveGameOption1Request Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new(metadata, ClientSettingsPayload.Decode(body, 150));
}
internal sealed class SaveGameOption1MessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        SaveGameOption1PayloadCodec.Decode(metadata, body);
}
public sealed class SaveGameOption1Handler(ICommandBus commands) : ClientMessageHandler<SaveGameOption1Request>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameSaveGameOption1Command>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SaveGameOption1Request message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSaveGameOption1Command(context, message), ct).ConfigureAwait(false);
}
