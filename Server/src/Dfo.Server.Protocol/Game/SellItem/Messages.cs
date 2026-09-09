using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SellItem;

public sealed record SellItemRequest(GameMessageMetadata Metadata, InventoryContainerType Container,
    short SlotIndex, short Count, int NpcId, int FieldSum) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSellItem;
}

public sealed record SellItemResponse(ItemSaleReceipt? Receipt, byte ErrorCode = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSellItem;
}
