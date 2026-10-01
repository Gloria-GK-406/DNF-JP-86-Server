using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Protocol.Game.EquipmentDurability;

internal sealed class DecreaseDurabilityResponseEncoder(IInventorySlotMappingRuleService slots) : GameCmdMessageEncoder<DecreaseDurabilityResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DecreaseDurabilityResponse message) =>
        OnePayload(GameProtocol.DecreaseDurabilityType, EncodeBody(message, slots));

    internal static byte[] EncodeBody(DecreaseDurabilityResponse message,
        IInventorySlotMappingRuleService? slots = null) =>
        message.Receipt is { } receipt
            ? [1, checked((byte)(slots ?? new InventorySlotMappingRuleService()).ToGameSlot(receipt.Slot).Index)]
            : [0, message.ErrorCode];
}
