using System.Buffers.Binary;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.SecretShop;

namespace Dfo.Server.Protocol.Tests.Game.SecretShop;

public sealed class SecretShopCodecTests
{
    [Fact]
    public void The_native_purchase_and_window_report_lengths_are_exact()
    {
        var decoder = new SecretShopBuyDecoder();
        var buy = Assert.IsType<SecretShopBuyRequest>(decoder.Decode(GameMessageMetadata.Empty,
            [0xBF, 0x0B, 0, 0, 2, 0, 0, 0]));
        Assert.Equal(3007, buy.ItemId);
        Assert.Equal(2, buy.Count);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, new byte[16]));
        var window = new SecretShopWindowDecoder();
        Assert.True(Assert.IsType<SecretShopWindowReport>(window.Decode(GameMessageMetadata.Empty, [1])).Open);
        Assert.False(Assert.IsType<SecretShopWindowReport>(window.Decode(GameMessageMetadata.Empty, [0])).Open);
        Assert.Throws<InvalidDataException>(() => window.Decode(GameMessageMetadata.Empty, [2]));
    }

    [Fact]
    public void The_list_publishes_both_payment_forms_in_twenty_two_byte_rows()
    {
        var bytes = SecretShopItemsEncoder.EncodeBody(new([new(3033, 100, 0, 100), new(22163, 3300, 600, 1)]));
        Assert.Equal(48, bytes.Length);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes));
        Assert.Equal(3033, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(0, bytes[8]);
        Assert.Equal(100, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(9)));
        Assert.Equal(100, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(13)));
        Assert.Equal(22163, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(26)));
        Assert.Equal(1, bytes[30]);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(31)));
        Assert.Equal(600, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(39)));
        Assert.Equal(3300, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(43)));
        Assert.Equal(0, bytes[47]);
    }

    [Fact]
    public void The_refusal_keeps_the_native_result_envelope()
    {
        Assert.Equal(new byte[] { 0, 0x0A }, SecretShopBuyEncoder.EncodeBody(new(NpcShopRejection.InsufficientGold)));
        Assert.Equal(new byte[] { 0, 0x11 }, SecretShopBuyEncoder.EncodeBody(new(NpcShopRejection.ItemNotSold)));
    }
}
