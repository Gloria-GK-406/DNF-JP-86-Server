using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.CeraShopPurchase;

public sealed record CeraShopPurchaseRequest(GameMessageMetadata Metadata, CeraShopPurchaseInput Input) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameCeraShopPurchase;
}

public sealed record CeraShopPurchaseResponse(int? ProductId, byte ErrorCode = 1) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCeraShopPurchase;
}
