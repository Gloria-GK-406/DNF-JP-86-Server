using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.RepairItem;

public sealed record RepairItemRequest(GameMessageMetadata Metadata, InventoryContainerType Container,
    short SlotIndex, short MaterialSlot, byte AutoRepair, byte Reserved, byte QuickRepair) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameRepairItem;
}

public sealed record RepairItemResponse(EquipmentRepairReceipt? Receipt, byte ErrorCode = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameRepairItem;
}
