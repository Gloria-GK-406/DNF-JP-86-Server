using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SellItem;

public sealed record SellItemRequest(GameMessageMetadata Metadata, GameSlot Slot,
    short Count, int NpcId, int FieldSum) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSellItem;
}

public sealed record SellItemResponse(
    GameSlot? Slot, int Gold = 0, int Count = 0, byte ErrorCode = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSellItem;
}
