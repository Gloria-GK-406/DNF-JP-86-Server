using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.BuyItem;

public record BuyItemRequest(
    GameMessageMetadata Metadata,
    int ItemId,
    int RawCount,
    int ShopId,
    int NpcId) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameBuyItem;
}

public sealed record HousingBuyItemRequest(GameMessageMetadata Metadata, int ItemId, int RawCount, int ShopId, int NpcId)
    : BuyItemRequest(Metadata, ItemId, RawCount, ShopId, NpcId), IEffectSettlementRequest
{
    public Dfo.Server.Abstractions.Effects.ReplayProjectionNeeds MinimumReplayNeeds =>
        Dfo.Server.Abstractions.Effects.ReplayProjectionNeeds.Inventory | Dfo.Server.Abstractions.Effects.ReplayProjectionNeeds.MarriageLovePoints | Dfo.Server.Abstractions.Effects.ReplayProjectionNeeds.MarriageHousing;
}

public sealed record BuyItemResponse(NpcShopPurchaseReceipt? Receipt, byte ErrorCode = 0) : IGameCmdMessage
{
    /// <summary>The ordered delivery already projected the stack through NOTI 000E; ACK must not grant it again.</summary>
    public bool InventoryAlreadyProjected { get; init; }
    public ServerMessageType MessageType => ServerMessageType.GameBuyItem;
}
