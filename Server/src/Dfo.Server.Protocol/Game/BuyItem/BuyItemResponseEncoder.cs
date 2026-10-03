using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Protocol.Game.BuyItem;

internal sealed class BuyItemResponseEncoder : GameCmdMessageEncoder<BuyItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(BuyItemResponse message) =>
        OnePayload(GameProtocol.BuyItemType, EncodeBody(message));

    internal static byte[] EncodeBody(BuyItemResponse message)
    {
        if (message.Receipt is not { } receipt)
            return [0, message.ErrorCode];
        if (receipt.GrantedSlots.Count == 0 || receipt.Count <= 0)
            throw new InvalidDataException("A purchase acknowledgement needs its granted item and quantity.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(receipt.Gold);
        writer.Write(receipt.SkillPoints);
        writer.Write(0); // Reference BUY_ITEM wallet field; no gameplay meaning has been established.
        writer.Write(receipt.Cera);
        var first = receipt.GrantedSlots[0];
        var wire = new InventorySlotMappingRuleService().ToGameSlot(first.Slot);
        // BUY_ITEM's value is the added amount; the following inventory delta carries the final core.
        GameInventoryItemEntry.Write(writer,
            new InventoryItemState(null, wire.Index, first.Core.ToCore() with { Value = first.AddedCount }),
            receipt.PreparedAt, wire.Space);
        writer.Write((byte)1);
        writer.Write(receipt.ItemId);
        writer.Write(receipt.Count);
        return stream.ToArray();
    }
}
