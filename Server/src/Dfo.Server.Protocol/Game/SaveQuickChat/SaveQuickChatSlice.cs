using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SaveQuickChat;

public sealed record SaveQuickChatRequest(GameMessageMetadata Metadata, byte Bank, byte[] Payload) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSaveQuickChat;
}

internal sealed class SaveQuickChatDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < 5 || body[0] > 1) throw new InvalidDataException("Quick-chat bank must be 0 or 1.");
        return new SaveQuickChatRequest(metadata, body[0], ClientSettingsPayload.Decode(body[1..], 300));
    }
}

public sealed class SaveQuickChatHandler(ICommandBus commands) : ClientMessageHandler<SaveQuickChatRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameSaveQuickChatCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SaveQuickChatRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSaveQuickChatCommand(context, message), ct).ConfigureAwait(false);
}
