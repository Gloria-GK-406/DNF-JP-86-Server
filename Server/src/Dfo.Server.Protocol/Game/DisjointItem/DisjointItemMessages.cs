using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DisjointItem;

public sealed record DisjointItemRequest(
    GameMessageMetadata Metadata,
    short TargetSlotIndex,
    InventoryContainerType ItemSpace,
    short DisjointItemSlotIndex,
    int ContextValue) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameDisjointItem;
}

public sealed record DisjointItemResponse(
    EquipmentDisjointReceipt? Receipt,
    byte ErrorCode = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameDisjointItem;
}
