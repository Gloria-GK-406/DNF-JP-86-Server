using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed record DeleteItemRequest(
    GameMessageMetadata Metadata,
    InventoryContainerType Container,
    byte EntryCount,
    ushort OperationType,
    ushort SlotIndex,
    int ItemId,
    int DeleteCount,
    uint ReservedZero,
    byte SafeModeSkipIndex) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameDeleteItem;
}

public sealed record DeleteItemResponse(
    ItemDiscardReceipt? Receipt,
    byte ErrorCode = 0,
    InventoryContainerType Container = InventoryContainerType.Main) : IGameCmdMessage
{
    public const byte CannotDelete = 0x17;

    public ServerMessageType MessageType => ServerMessageType.GameDeleteItem;
}
