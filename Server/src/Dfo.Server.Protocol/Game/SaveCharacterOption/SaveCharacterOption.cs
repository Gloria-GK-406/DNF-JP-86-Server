using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game.SaveCharacterOption;

public sealed record SaveCharacterOptionRequest(GameMessageMetadata Metadata, byte[] Body) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSaveCharacterOption;
}
internal sealed class SaveCharacterOptionDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 516 && BinaryPrimitives.ReadUInt32LittleEndian(body) == 512
            ? new SaveCharacterOptionRequest(metadata,body.ToArray())
            : throw new InvalidDataException("A21 character options require u32(512) and 512 bytes.");
}
public sealed class SaveCharacterOptionHandler(ICommandBus commands) : ClientMessageHandler<SaveCharacterOptionRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameSaveCharacterOptionCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SaveCharacterOptionRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSaveCharacterOptionCommand(context, message), ct).ConfigureAwait(false);
}
