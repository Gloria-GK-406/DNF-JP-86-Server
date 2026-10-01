using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.EnchantByBead;

public sealed record EnchantByBeadRequest(
    GameMessageMetadata Metadata,
    InventoryContainerType BeadList,
    short BeadSlot,
    InventoryContainerType TargetList,
    short TargetSlot) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => ClientMessageType.GameEnchantByBead;
}

public sealed record EnchantByBeadResponse(
    bool Success, byte ErrorCode = 0,
    InventoryContainerType TargetList = InventoryContainerType.Equipment,
    short TargetSlot = -1) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEnchantByBead;
}
