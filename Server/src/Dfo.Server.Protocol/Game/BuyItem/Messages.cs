using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.BuyItem;

public sealed record BuyItemRequest(
    GameMessageMetadata Metadata,
    int ItemId,
    int RawCount,
    int ShopId,
    int NpcId) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameBuyItem;
}

public sealed record BuyItemResponse(NpcShopPurchaseReceipt? Receipt, byte ErrorCode = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameBuyItem;
}
