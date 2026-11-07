using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.EquipmentGrowth;

/// <summary>
/// NOTI 0x01E1 <c>GROWTH_ITEM_RENEW_DATA</c>: a growth weapon's experience changed on the same template.
/// </summary>
/// <remarks>
/// Layout from the A21 handler at VA 0x01188130 (ClientStatic, Confirmed): <c>u8 container |
/// u16 slot | u16 gained | u16 experience | u8 maintainedFatigue</c>. The client clamps the experience
/// to the star's maximum and plays its gain animation when <c>gained</c> is positive.
/// </remarks>
public sealed record GrowthItemRenewNotice(GameSlot Slot, ushort Gained, ushort Experience, byte MaintainedFatigue)
    : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGrowthItemRenewData;
}

/// <summary>One piece in NOTI 0x01E2.</summary>
public sealed record GrowthItemChangeEntry(GameSlot Slot, int ItemId, ushort Experience, byte MaintainedFatigue);

/// <summary>
/// NOTI 0x01E2 <c>GROWTH_ITEM_CHANGE_DATA</c>: growth weapons turned into another star (type 1) or had
/// their growth state rewritten (type 2).
/// </summary>
/// <remarks>
/// Layout from the A21 handler at VA 0x0116B800 (ClientStatic): <c>u8 type | u8 count |
/// count × { u8 container, u16 slot, u32 itemId, u16 experience, u8 maintainedFatigue }</c>.
/// Type 1 rebuilds the item from the new template id; type 2 only rewrites the two growth fields.
/// </remarks>
public sealed record GrowthItemChangeNotice(EquipmentGrowthChangeKind Kind, IReadOnlyList<GrowthItemChangeEntry> Items)
    : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGrowthItemChangeData;
}

internal sealed class GrowthItemRenewNoticeEncoder : GameNotiMessageEncoder<GrowthItemRenewNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GrowthItemRenewNotice message)
    {
        var body = new byte[8];
        body[0] = (byte)message.Slot.Space;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1), checked((ushort)message.Slot.Index));
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(3), message.Gained);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(5), message.Experience);
        body[7] = message.MaintainedFatigue;
        return OnePayload(message, body);
    }
}

internal sealed class GrowthItemChangeNoticeEncoder : GameNotiMessageEncoder<GrowthItemChangeNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GrowthItemChangeNotice message)
    {
        if (message.Kind is not (EquipmentGrowthChangeKind.Upgraded or EquipmentGrowthChangeKind.Reset))
            throw new ArgumentOutOfRangeException(nameof(message), "0x01E2 carries star changes and resets only.");
        if (message.Items.Count is < 1 or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(message), "0x01E2 carries one to 255 pieces.");
        var body = new byte[2 + 10 * message.Items.Count];
        body[0] = (byte)message.Kind;
        body[1] = (byte)message.Items.Count;
        for (var i = 0; i < message.Items.Count; i++)
        {
            var item = message.Items[i];
            var at = body.AsSpan(2 + 10 * i);
            at[0] = (byte)item.Slot.Space;
            BinaryPrimitives.WriteUInt16LittleEndian(at[1..], checked((ushort)item.Slot.Index));
            BinaryPrimitives.WriteInt32LittleEndian(at[3..], item.ItemId);
            BinaryPrimitives.WriteUInt16LittleEndian(at[7..], item.Experience);
            at[9] = item.MaintainedFatigue;
        }
        return OnePayload(message, body);
    }
}

/// <summary>Turns a committed growth change into the frame the client expects for it.</summary>
internal static class EquipmentGrowthFrames
{
    public static IGameServerMessage Of(EquipmentGrowthChange change)
    {
        var slot = A21InventoryLayout.ToGameSlot(change.Slot);
        return change.Kind == EquipmentGrowthChangeKind.Gained
            ? new GrowthItemRenewNotice(slot, change.Gained, change.Experience, change.MaintainedFatigue)
            : new GrowthItemChangeNotice(change.Kind, [new(slot, change.ItemId, change.Experience, change.MaintainedFatigue)]);
    }
}
