using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.EquipmentGrowth;

namespace Dfo.Server.Protocol.Tests.Game.EquipmentGrowth;

public sealed class EquipmentGrowthNoticeTests
{
    private const int Header = 15;

    [Fact]
    public void Renew_notice_writes_the_a21_eight_byte_body()
    {
        var frame = Assert.Single(new GrowthItemRenewNoticeEncoder().Encode(
            new GrowthItemRenewNotice(new(InventoryContainerType.Equipment, 12), 5, 1794, 30)));
        Assert.Equal(0x00, frame[0]);
        Assert.Equal((ushort)0x01E1, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(new byte[] { 3, 12, 0, 5, 0, 0x02, 0x07, 30 }, frame[Header..]);
    }

    [Fact]
    public void Change_notice_writes_type_count_and_ten_byte_entries()
    {
        var frame = Assert.Single(new GrowthItemChangeNoticeEncoder().Encode(new GrowthItemChangeNotice(
            EquipmentGrowthChangeKind.Upgraded, [new(new(InventoryContainerType.Equipment, 12), 101000043, 0, 7)])));
        Assert.Equal((ushort)0x01E2, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        var body = frame[Header..];
        Assert.Equal(new byte[] { 1, 1, 3, 12, 0 }, body[..5]);
        Assert.Equal(101000043, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5)));
        Assert.Equal(new byte[] { 0, 0, 7 }, body[9..]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrowthItemChangeNoticeEncoder().Encode(
            new GrowthItemChangeNotice(EquipmentGrowthChangeKind.Gained, [new(new(InventoryContainerType.Equipment, 12), 1, 0, 0)])));
    }

    [Fact]
    public void A_committed_change_maps_to_the_frame_its_kind_uses()
    {
        var weapon = new BusinessSlot(BusinessInventorySpace.WornEquipment, 0);
        Assert.Equal(new GrowthItemRenewNotice(new(InventoryContainerType.Equipment, 12), 5, 10, 3),
            EquipmentGrowthFrames.Of(new(weapon, 3000, EquipmentGrowthChangeKind.Gained, 5, 10, 3)));
        var reset = Assert.IsType<GrowthItemChangeNotice>(
            EquipmentGrowthFrames.Of(new(weapon, 3000, EquipmentGrowthChangeKind.Reset, 0, 8, 0)));
        Assert.Equal(EquipmentGrowthChangeKind.Reset, reset.Kind);
        Assert.Equal(new GrowthItemChangeEntry(new(InventoryContainerType.Equipment, 12), 3000, 8, 0), Assert.Single(reset.Items));
    }
}
