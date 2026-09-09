using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CeraShopPurchase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.CeraShopPurchase;

public sealed class CeraShopCodecTests
{
    [Fact]
    public void Variable_options_and_each_products_coupon_tail_are_decoded_with_one_cursor()
    {
        var body = TwoProductBody();
        var request = Assert.IsType<CeraShopPurchaseRequest>(new CeraShopPurchaseMessageDecoder().Decode(GameMessageMetadata.Empty, body));
        Assert.Null(request.Input.InvalidReason);
        Assert.Equal(2, request.Input.Entries.Length);
        var first = request.Input.Entries[0];
        Assert.Equal(102805, first.ProductId);
        Assert.Equal(new(39032, 2), Assert.Single(first.AvatarChoices));
        Assert.Equal(new(2683477, 0, 1), Assert.Single(first.Selections));
        Assert.Equal((123, 8), (first.CouponItemId, (int)first.CouponSlot));
        Assert.Equal((110000, 555, 9), (request.Input.Entries[1].ProductId,
            request.Input.Entries[1].CouponItemId, (int)request.Input.Entries[1].CouponSlot));
    }

    [Fact]
    public void Every_truncation_and_trailing_bytes_reject_the_entire_cart_without_a_valid_prefix()
    {
        var body = TwoProductBody();
        var decoder = new CeraShopPurchaseMessageDecoder();
        for (var length = 0; length < body.Length; length++)
        {
            var bad = Assert.IsType<CeraShopPurchaseRequest>(decoder.Decode(GameMessageMetadata.Empty, body.AsSpan(0, length)));
            Assert.NotNull(bad.Input.InvalidReason);
            Assert.Empty(bad.Input.Entries);
        }
        foreach (var invalid in new byte[][] { [0, 0, 0, 0], [0, 1], [.. body, 0], [.. body, .. new byte[16]] })
        {
            var bad = Assert.IsType<CeraShopPurchaseRequest>(decoder.Decode(GameMessageMetadata.Empty, invalid));
            Assert.NotNull(bad.Input.InvalidReason);
            Assert.Empty(bad.Input.Entries);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(11)]
    public void A21_failure_reader_consumes_success_error_flag_and_five_integers(byte error)
    {
        var body = CeraShopPurchaseResponseEncoder.EncodeBody(new(null, error));
        Assert.Equal(23, body.Length);
        using var reader = new BinaryReader(new MemoryStream(body));
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal(error, reader.ReadByte());
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal(-1, reader.ReadInt32()); Assert.Equal(-1, reader.ReadInt32());
        Assert.Equal(0, reader.ReadInt32()); Assert.Equal(0, reader.ReadInt32()); Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(body.Length, reader.BaseStream.Position);
    }

    [Fact]
    public void Success_names_the_product_without_misusing_display_override_or_extra_items()
    {
        var body = CeraShopPurchaseResponseEncoder.EncodeBody(new(110000));
        Assert.Equal(24, body.Length);
        Assert.Equal("0100FFFFFFFFB0AD01000000000000000000000000000000", Convert.ToHexString(body));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(18)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(22)));
    }

    private static byte[] TwoProductBody()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(new byte[] { 0, 0, 2, 1 });
        writer.Write(new byte[] { 0, 2, 7 }); writer.Write(102805);
        writer.Write((byte)1); writer.Write(39032); writer.Write((byte)2);
        writer.Write((byte)1); writer.Write(2683477); writer.Write((ushort)0); writer.Write((ushort)1);
        writer.Write(123); writer.Write((ushort)8);
        writer.Write(new byte[] { 0, 255, 255 }); writer.Write(110000);
        writer.Write((byte)0); writer.Write((byte)0); writer.Write(555); writer.Write((ushort)9);
        return stream.ToArray();
    }

    [Fact]
    public void Captured_nineteen_byte_purchase_reaches_the_registered_decoder()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x0040);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        var message = Assert.IsType<CeraShopPurchaseRequest>(decoder.Decode(GameMessageMetadata.Empty,
            Convert.FromHexString("0000010000FFFFB0AD01000000000000000000")));
        Assert.Null(message.Input.InvalidReason);
        var entry = Assert.Single(message.Input.Entries);
        Assert.Equal(110000, entry.ProductId);
        Assert.Equal(255, entry.Attribute);
        Assert.Equal(255, entry.Variant);
        Assert.Empty(entry.AvatarChoices);
        Assert.Empty(entry.Selections);
        Assert.Equal(0, entry.CouponItemId);
    }
}
