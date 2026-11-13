using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.PrivateStore;
using Xunit;

namespace Dfo.Server.Protocol.Tests.Game.PrivateStore;

/// <summary>Bodies captured from the A21 client on 2026-11-14 (test host log, character 1).</summary>
public sealed class PrivateStoreCodecTests
{
    private static readonly GameMessageMetadata Metadata = GameMessageMetadata.Empty;

    private static BusinessSlot Slot(byte list, int slot) =>
        A21InventoryLayout.ToBusinessSlot(new GameSlot((InventoryContainerType)list, slot));

    [Fact]
    public void Create_reads_town_area_position_and_no_doll()
    {
        var request = PrivateStorePayloadCodec.DecodeCreate(Metadata, Convert.FromHexString("0202A9003501FFFF"));

        Assert.False(request.Malformed);
        Assert.Equal((2, 2, 169, 309), ((int)request.TownId, (int)request.AreaId, (int)request.X, (int)request.Y));
        Assert.Null(request.Doll);
    }

    [Fact]
    public void Display_reads_seven_lines_and_keeps_the_count_word_as_sent()
    {
        var body = Convert.FromHexString(
            "09000000E6B58BE8AF95E794A807000001000000000B000000000001000100000000130021E88626020001000000007B0002000000"
            + "030003000000007C0014000000040004000000007D000100000005000500000000470011000000060002000000001400A28E4065FFFF");

        var request = PrivateStorePayloadCodec.DecodeDisplay(Metadata, body);

        Assert.False(request.Malformed);
        Assert.Equal("测试用"u8.ToArray(), request.Title);
        Assert.Equal(
            [
                (0, 1L, Slot(0, 11), 0),
                (1, 1L, Slot(0, 19), 0x2686E821),
                (2, 1L, Slot(0, 123), 2),
                (3, 3L, Slot(0, 124), 20),
                (4, 4L, Slot(0, 125), 1),
                (5, 5L, Slot(0, 71), 17),
                (6, 2L, Slot(0, 20), 0x65408EA2),
            ],
            request.Lines.Select(line => ((int)line.DisplaySlot, line.UnitPrice, line.Source, line.Count)));
    }

    [Fact]
    public void Display_with_a_short_or_padded_body_is_malformed()
    {
        var body = Convert.FromHexString(
            "0F000000E6B58BE8AF95E794A8E59586E5BA97020000A0860100000B0000000000010050C3000000130021E88626FFFF");
        Assert.False(PrivateStorePayloadCodec.DecodeDisplay(Metadata, body).Malformed);
        Assert.True(PrivateStorePayloadCodec.DecodeDisplay(Metadata, body[..^1]).Malformed);
        Assert.True(PrivateStorePayloadCodec.DecodeDisplay(Metadata, [.. body, 0]).Malformed);
    }

    [Fact]
    public void Enter_reads_the_owner_as_two_or_four_bytes()
    {
        Assert.Equal(60, PrivateStorePayloadCodec.DecodeSimple(Metadata, [60, 0], ClientMessageType.GamePrivateStoreEnter).Owner);
        Assert.Equal(60, PrivateStorePayloadCodec.DecodeSimple(Metadata, [60, 0, 0, 0], ClientMessageType.GamePrivateStoreEnter).Owner);
        Assert.True(PrivateStorePayloadCodec.DecodeSimple(Metadata, [60, 0, 1, 0], ClientMessageType.GamePrivateStoreEnter).Malformed);
        Assert.False(PrivateStorePayloadCodec.DecodeSimple(Metadata, [], ClientMessageType.GamePrivateStoreRemove).Malformed);
    }

    [Fact]
    public void Buy_reads_display_slot_and_count()
    {
        var request = PrivateStorePayloadCodec.DecodeLine(Metadata, [3, 0, 5, 0, 0, 0], buy: true);
        Assert.Equal((false, (byte)3, 5), (request.Malformed, request.DisplaySlot, request.Count));
        Assert.True(PrivateStorePayloadCodec.DecodeLine(Metadata, [14, 0, 5, 0, 0, 0], buy: true).Malformed);
    }
}
