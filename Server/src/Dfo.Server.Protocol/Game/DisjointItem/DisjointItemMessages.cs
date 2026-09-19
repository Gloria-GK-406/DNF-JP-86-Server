using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DisjointItem;

public sealed record DisjointItemRequest(
    GameMessageMetadata Metadata,
    GameSlot TargetSlot,
    short DisjointItemSlotIndex,
    int ContextValue) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameDisjointItem;
}

public sealed record DisjointItemMaterial(GameSlot Slot, int ItemId, int Count);

public sealed record DisjointItemResponse(
    GameSlot? TargetSlot,
    IReadOnlyList<DisjointItemMaterial>? Materials = null,
    byte ErrorCode = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameDisjointItem;
}
