using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.InventorySort;

public sealed record SortItemRequest(GameMessageMetadata Metadata, byte ListType, byte Category, byte Condition)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSortItem;
}
public sealed record SortItemResponse(byte ListType, bool Success, byte ErrorCode = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSortItem;
}
internal sealed class SortItemMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 3) throw new InvalidDataException($"SORT_ITEM requires 3 body bytes; received {body.Length}.");
        return new SortItemRequest(metadata, body[0], body[1], body[2]);
    }
}
internal sealed class SortItemResponseEncoder : GameCmdMessageEncoder<SortItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SortItemResponse message) =>
        OnePayload(GameProtocol.SortItemType, EncodeBody(message));
    internal static byte[] EncodeBody(SortItemResponse message) => message.Success
        ? [1, message.ListType] : [0, message.ErrorCode, message.ListType];
}
