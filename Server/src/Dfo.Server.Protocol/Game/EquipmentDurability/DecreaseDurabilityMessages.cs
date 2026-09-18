using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.EquipmentDurability;

public sealed record DecreaseDurabilityRequest(GameMessageMetadata Metadata, byte SlotIndex)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameDecreaseDurability;
}

public sealed record DecreaseDurabilityResponse(EquipmentDurabilityReceipt? Receipt, byte ErrorCode = 0)
    : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameDecreaseDurability;
}
