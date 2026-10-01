using System.Buffers.Binary;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.AvatarPackageOpening;
using Dfo.Server.Protocol.Game.MagicBoxBatchOpening;
using Dfo.Server.Protocol.Game.MagicBoxSingleOpening;
using Dfo.Server.Protocol.Game.PackageOpening;

namespace Dfo.Server.Protocol.Tests.Game.PackageOpening;

public sealed class PackageOpeningCodecTests
{
    private static readonly OpeningRewardDisplay Reward = new(new(BusinessInventorySpace.Consumable, 5), 10007368, 2, 123, 7, 0x12345678);
    private static readonly OpeningInventoryReceipt Receipt = new(
        SelectedSessionIdentity.Create(new(1), new(2), new("opening-codec"), 1, 10011), [], [], [Reward], DateTimeOffset.UnixEpoch);

    [Fact]
    public void Live_sixty_box_request_has_fifteen_bytes_and_two_distinct_item_identifiers()
    {
        // Actual A21 request, 2026-09-09, seq 34/35. Static census incorrectly combined branches.
        var request = Assert.IsType<MagicBoxBatchOpeningRequest>(new MagicBoxBatchOpeningDecoder().Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("00420048B398007A0047B398003C00")));
        Assert.Equal(new(0, 66, 10007368, 122, 10007367, 60), request.Input);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(16)]
    [InlineData(18)]
    public void Batch_has_no_prefix_or_census_layout_fallback(int length) =>
        Assert.Null(Assert.IsType<MagicBoxBatchOpeningRequest>(new MagicBoxBatchOpeningDecoder().Decode(
            GameMessageMetadata.Empty, new byte[length])).Input);

    [Fact]
    public void Single_box_without_material_uses_the_signed_ffff_sentinel()
    {
        var request = Assert.IsType<MagicBoxSingleOpeningRequest>(new MagicBoxSingleOpeningDecoder().Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("044200FFFF")));
        Assert.Equal(new(4, 66, 0, -1, 0, 1), request.Input);
    }

    [Fact]
    public void Ordinary_selection_and_avatar_selection_are_distinct_typed_inputs()
    {
        var decoder = new PackageOpeningDecoder();
        var plain = Assert.IsType<PackageOpeningRequest>(decoder.Decode(GameMessageMetadata.Empty, Convert.FromHexString("4200030148B3980000")));
        Assert.Equal(66, plain.Input!.SourceSlot);
        Assert.Equal(10007368, Assert.Single(plain.Input.SelectedItems));
        Assert.Null(plain.AvatarInput);
        var avatar = Assert.IsType<PackageOpeningRequest>(decoder.Decode(GameMessageMetadata.Empty, Convert.FromHexString("4200030148B398000148B3980007")));
        Assert.Equal(new AvatarPackageChoice(10007368, 7), Assert.Single(avatar.AvatarInput!.Choices));
    }

    [Theory]
    [InlineData("4200030148B39800")]
    [InlineData("4200030148B398000147B3980007")]
    [InlineData("4200030148B398000000")]
    public void Selection_requires_a_complete_unambiguous_matching_avatar_tail(string hex) =>
        Assert.Null(Assert.IsType<PackageOpeningRequest>(new PackageOpeningDecoder().Decode(
            GameMessageMetadata.Empty, Convert.FromHexString(hex))).Input);

    [Fact]
    public void Avatar_header_is_source_slot_then_count_then_item_and_ability()
    {
        var request = Assert.IsType<AvatarPackageOpeningRequest>(new AvatarPackageOpeningDecoder().Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("42000148B3980007")));
        Assert.Equal(66, request.Input!.SourceSlot);
        Assert.Equal(new AvatarPackageChoice(10007368, 7), Assert.Single(request.Input.Choices));
    }

    [Theory]
    [InlineData("42000148B39800")]
    [InlineData("42000148B398000700")]
    [InlineData("420021")]
    public void Avatar_does_not_accept_a_partial_or_extended_group(string hex) =>
        Assert.Null(Assert.IsType<AvatarPackageOpeningRequest>(new AvatarPackageOpeningDecoder().Decode(
            GameMessageMetadata.Empty, Convert.FromHexString(hex))).Input);

    [Fact]
    public void Native_batch_success_has_two_counted_forty_byte_lists_and_the_middle_reserved_word()
    {
        var input = new MagicBoxOpeningInput(0, 66, 10007368, 122, 10007367, 60);
        var result = new MagicBoxOpeningResult(OpeningRejection.None, Receipt, [Reward], [Reward with { Count = 1 }], 7, 3);
        var body = Body(Assert.Single(new MagicBoxBatchOpeningEncoder().Encode(new MagicBoxBatchOpeningResponse(input, result))), 1, 0x0450);
        Assert.Equal(95, body.Length);
        Assert.Equal(Convert.FromHexString("0100013C0042007A000100"), body[..11]);
        AssertRewardRow(body.AsSpan(11, 40), 2);
        Assert.Equal(Convert.FromHexString("00000100"), body[51..55]);
        AssertRewardRow(body.AsSpan(55, 40), 1);
    }

    [Fact]
    public void Native_single_success_has_one_list_without_batch_count_or_middle_word()
    {
        var input = new MagicBoxOpeningInput(4, 66, 0, -1, 0, 1);
        var result = new MagicBoxOpeningResult(OpeningRejection.None, Receipt, [Reward], []);
        var body = Body(Assert.Single(new MagicBoxSingleOpeningEncoder().Encode(new MagicBoxSingleOpeningResponse(input, result))), 1, 0x00D0);
        Assert.Equal(49, body.Length);
        Assert.Equal(Convert.FromHexString("0104004200FFFF0100"), body[..9]);
        AssertRewardRow(body.AsSpan(9), 2);
    }

    [Fact]
    public void Package_popup_and_avatar_completion_have_different_success_shapes()
    {
        var result = new PackageOpeningResult(OpeningRejection.None, Receipt);
        var expected = Convert.FromHexString("0142000000000000000000010048B3980002000000");
        Assert.Equal(expected, Body(Assert.Single(new PackageOpeningEncoder().Encode(new PackageOpeningResponse(66, result))), 1, 0x00A0));
        Assert.Equal(expected, Body(Assert.Single(new OpeningRewardPopupEncoder().Encode(new OpeningRewardPopup(66, [Reward]))), 1, 0x00A0));
        Assert.Equal(Convert.FromHexString("014200"), Body(Assert.Single(new AvatarPackageOpeningEncoder().Encode(new AvatarPackageOpeningResponse(66, result))), 1, 0x0207));
    }

    [Fact]
    public void Failure_shapes_do_not_contain_success_lists()
    {
        Assert.Equal(new byte[] { 0, 0x16 }, Body(Assert.Single(new MagicBoxSingleOpeningEncoder().Encode(new MagicBoxSingleOpeningResponse(null, new(OpeningRejection.MissingMaterial)))), 1, 0x00D0));
        Assert.Equal(new byte[] { 0, 2 }, Body(Assert.Single(new AvatarPackageOpeningEncoder().Encode(new AvatarPackageOpeningResponse(66, new(OpeningRejection.InvalidChoice)))), 1, 0x0207));
        Assert.Equal(Convert.FromHexString("0000FFFFFFFFFFFFFFFF000000000000000000000000"),
            Body(Assert.Single(new PackageOpeningEncoder().Encode(new PackageOpeningResponse(66, new(OpeningRejection.InvalidChoice)))), 1, 0x00A0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Full_inventory_uses_the_box_inventory_message_instead_of_the_anti_addiction_branch(bool batch)
    {
        // A21 D16F44 / D17F65: 0x7E selects string 70005 (anti-addiction).
        // The ordinary failure branch selects string 746 (inventory full).
        var result = new MagicBoxOpeningResult(OpeningRejection.NoRoom);
        var frame = batch
            ? Assert.Single(new MagicBoxBatchOpeningEncoder().Encode(new MagicBoxBatchOpeningResponse(null, result)))
            : Assert.Single(new MagicBoxSingleOpeningEncoder().Encode(new MagicBoxSingleOpeningResponse(null, result)));
        Assert.Equal(new byte[] { 0, 2 }, Body(frame, 1, batch ? (ushort)0x0450 : (ushort)0x00D0));
    }

    [Fact]
    public void Premium_grant_is_notification_subtype_two_with_signed_eight_byte_remaining_seconds()
    {
        var body = Body(Assert.Single(new PremiumGrantedEncoder().Encode(new PremiumGrantedMessage(new(4, 86500), DateTimeOffset.FromUnixTimeSeconds(100)))), 0, 0x0042);
        Assert.Equal(Convert.FromHexString("0200048051010000000000"), body);
        Assert.Equal(Convert.FromHexString("0200040000000000000000"), Body(Assert.Single(new PremiumGrantedEncoder().Encode(
            new PremiumGrantedMessage(new(4, 99), DateTimeOffset.FromUnixTimeSeconds(100)))), 0, 0x0042));
    }

    private static byte[] Body(byte[] frame, byte selector, ushort type)
    {
        Assert.Equal(selector, frame[0]);
        Assert.Equal(type, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(frame.Length, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(3)));
        return frame[15..];
    }

    private static void AssertRewardRow(ReadOnlySpan<byte> row, int count)
    {
        Assert.Equal(Convert.FromHexString("460048B39800"), row[..6].ToArray());
        Assert.Equal(count, BinaryPrimitives.ReadInt32LittleEndian(row[6..]));
        Assert.Equal(Convert.FromHexString("7B000778563412"), row[10..17].ToArray());
        Assert.Equal(new byte[23], row[17..].ToArray());
    }
}
