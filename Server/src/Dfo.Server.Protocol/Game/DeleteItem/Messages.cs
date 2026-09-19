using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed record DeleteItemRequest(
    GameMessageMetadata Metadata,
    GameSlot Slot,
    byte EntryCount,
    ushort OperationType,
    int ItemId,
    int DeleteCount,
    uint ReservedZero,
    byte SafeModeSkipIndex) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameDeleteItem;
}

public sealed record DeleteItemResponse(
    GameSlot Slot,
    int AppliedCount = 0,
    bool Success = false,
    byte ErrorCode = 0) : IGameCmdMessage
{
    public const byte CannotDelete = 0x17;

    public ServerMessageType MessageType => ServerMessageType.GameDeleteItem;
}
