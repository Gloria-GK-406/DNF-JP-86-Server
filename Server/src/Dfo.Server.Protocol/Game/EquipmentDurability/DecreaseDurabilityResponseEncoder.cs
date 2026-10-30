using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.EquipmentDurability;

internal sealed class DecreaseDurabilityResponseEncoder : GameCmdMessageEncoder<DecreaseDurabilityResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DecreaseDurabilityResponse message) =>
        OnePayload(GameProtocol.DecreaseDurabilityType, EncodeBody(message));

    internal static byte[] EncodeBody(DecreaseDurabilityResponse message) =>
        message.Receipt is { } receipt
            ? [1, checked((byte)A21InventoryLayout.ToGameSlot(receipt.Slot).Index)]
            : [0, message.ErrorCode];
}
