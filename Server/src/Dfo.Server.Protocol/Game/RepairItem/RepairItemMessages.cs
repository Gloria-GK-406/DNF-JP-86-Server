using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.RepairItem;

public sealed record RepairItemRequest(GameMessageMetadata Metadata, GameSlot? Slot,
    short MaterialSlot, byte AutoRepair, byte Reserved, byte QuickRepair,
    InventoryContainerType RepairAllSpace = InventoryContainerType.Main) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameRepairItem;
}

public sealed record RepairItemResponse(
    GameSlot? Slot,
    int Gold = 0,
    byte ErrorCode = 0,
    InventoryContainerType RepairAllSpace = InventoryContainerType.Main) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameRepairItem;
}
