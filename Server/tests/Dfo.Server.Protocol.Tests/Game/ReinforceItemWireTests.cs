using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.ReinforceItem;

namespace Dfo.Server.Protocol.Tests.Game;

[Trait("Category", "Unit")]
public sealed class ReinforceItemWireTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Equipped_target_is_a_separate_byte_from_upgrade_method(byte method)
    {
        var body = new byte[16];
        body[0] = method;
        body[1] = 3;
        body[2] = 12;
        var request = Assert.IsType<ReinforceItemRequest>(
            new ReinforceItemMessageDecoder().Decode(GameMessageMetadata.Empty, body));
        Assert.Equal(method, request.Method);
        Assert.Equal(InventoryContainerType.Equipment, request.TargetList);
        var ack = ReinforceItemResponseEncoder.EncodeBody(new(true, Method: method,
            TargetSlot: 12, TargetList: request.TargetList));
        Assert.Equal((byte)3, ack[14]);
        Assert.Equal((short)12, BitConverter.ToInt16(ack, 15));
    }

    [Fact]
    public void Amplification_ack_preserves_method_one_and_the_shared_body_layout()
    {
        var body = ReinforceItemResponseEncoder.EncodeBody(new(true, Method: 1,
            MaterialSlot: 121, MaterialRemaining: 9, OldLevel: 0,
            Outcome: EquipmentReinforcementOutcome.Succeeded, NewLevel: 1, TargetSlot: 9));
        Assert.Equal(Convert.FromHexString("0101790009000000FFFF00000001000900FFFF"), body);
    }

    [Fact]
    public void Decoder_reads_method_slots_template_and_length_prefixed_name()
    {
        byte[] body =
        [
            0x00, 0x00, 0x09, 0x00, 0xB8, 0x0B, 0x00, 0x00,
            0x66, 0x01, 0xFF, 0xFF, 0x04, 0x00, 0x00, 0x00,
            0x74, 0x65, 0x73, 0x74, 0x00,
        ];

        var request = Assert.IsType<ReinforceItemRequest>(
            new ReinforceItemMessageDecoder().Decode(GameMessageMetadata.Empty, body));

        Assert.Equal((byte)0, request.Method);
        Assert.Equal((short)9, request.TargetSlot);
        Assert.Equal(3000, request.ExpectedItemId);
        Assert.Equal((short)358, request.MaterialSlot);
        Assert.Equal((short)-1, request.OptionalTicketSlot);
        Assert.Equal([0x74, 0x65, 0x73, 0x74], request.ReportedItemNameBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void Decoder_refuses_a_short_body(int length) =>
        Assert.Throws<InvalidDataException>(() =>
            new ReinforceItemMessageDecoder().Decode(
                GameMessageMetadata.Empty, new byte[length]));

    [Fact]
    public void Decoder_refuses_name_length_past_the_body()
    {
        var body = new byte[16];
        body[12] = 1;
        Assert.Throws<InvalidDataException>(() =>
            new ReinforceItemMessageDecoder().Decode(GameMessageMetadata.Empty, body));
    }

    [Fact]
    public void Decoder_keeps_non_utf8_reported_name_bytes_without_using_them_as_authority()
    {
        var body = new byte[18];
        body[12] = 2;
        body[16] = 0x81;
        body[17] = 0x40;

        var request = Assert.IsType<ReinforceItemRequest>(
            new ReinforceItemMessageDecoder().Decode(GameMessageMetadata.Empty, body));

        Assert.Equal([0x81, 0x40], request.ReportedItemNameBytes);
    }

    [Fact]
    public void Success_ack_matches_the_A21_handler_read_sequence()
    {
        var body = ReinforceItemResponseEncoder.EncodeBody(new(true,
            MaterialSlot: 121, MaterialRemaining: 10, OldLevel: 4,
            Outcome: EquipmentReinforcementOutcome.Succeeded, NewLevel: 5, TargetSlot: 9));

        Assert.Equal(Convert.FromHexString(
            "010079000A000000FFFF00040005000900FFFF"), body);
    }

    [Fact]
    public void Destruction_ack_appends_count_and_ten_bytes_per_reward()
    {
        var body = ReinforceItemResponseEncoder.EncodeBody(new(true,
            MaterialSlot: 121, MaterialRemaining: 0, OldLevel: 11,
            Outcome: EquipmentReinforcementOutcome.Destroyed, NewLevel: 0,
            TargetSlot: 9,
            DestroyRewards: [new(new(InventoryContainerType.Main, 122), 3243, 4)]));

        Assert.Equal(Convert.FromHexString(
            "0100790000000000FFFF000B0300000900FFFF"
            + "017A00AB0C000004000000"), body);
    }

    [Fact]
    public void Error_ack_has_both_bytes_of_the_client_cmd_envelope() =>
        Assert.Equal([0, 0x0A], ReinforceItemResponseEncoder.EncodeBody(new(false, 0x0A)));
}
