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
        if (message.InventoryAlreadyProjected && !first.Core.IsPile)
            throw new InvalidDataException("Only a pre-projected stack may use a zero-increment purchase acknowledgement.");
        // Native ACK adds Data to an existing pile. Its preceding 000E already applied the real
        // grant and emitted the native gain text, so this confirmation must add zero.
        GameInventoryItemEntry.Write(writer,
            new InventoryItemState(null, wire.Index, first.Core.ToCore() with
                { Value = message.InventoryAlreadyProjected ? 0 : first.AddedCount }),
            receipt.PreparedAt, wire.Space);
        // D35E57..D35E6C treats tail pairs as absolute balances, not purchase quantities.
        // Material balances are projected separately; never overwrite the purchased pile here.
        writer.Write((byte)0);
        return stream.ToArray();
    }
}
