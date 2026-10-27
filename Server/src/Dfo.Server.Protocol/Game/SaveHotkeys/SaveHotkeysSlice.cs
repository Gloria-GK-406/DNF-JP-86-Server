using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SaveHotkeys;

public sealed record SaveHotkeysRequest(GameMessageMetadata Metadata, byte[] Payload) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSaveHotkeys;
}

internal sealed class SaveHotkeysDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new SaveHotkeysRequest(metadata, ClientSettingsPayload.Decode(body));
}

public sealed class SaveHotkeysHandler(ICommandBus commands) : ClientMessageHandler<SaveHotkeysRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameSaveHotkeysCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SaveHotkeysRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSaveHotkeysCommand(context, message), ct).ConfigureAwait(false);
}
