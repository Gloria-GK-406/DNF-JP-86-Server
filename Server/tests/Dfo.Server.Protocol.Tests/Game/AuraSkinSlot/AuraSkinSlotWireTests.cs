using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.AuraSkinSlot;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.AuraSkinSlot;

/// <summary>CMD 0x0357 bytes and where USERINFO subtype 1 carries the opened flag (#327).</summary>
[Trait("Category", "Unit")]
public sealed class AuraSkinSlotWireTests
{
    [Fact]
    public void Request_is_one_i32_main_inventory_slot()
    {
        var request = Assert.IsType<AuraSkinSlotRequest>(new AuraSkinSlotDecoder().Decode(GameMessageMetadata.Empty, [0x2A, 0x01, 0, 0]));
        Assert.True(request.Complete);
        Assert.Equal(298, request.SourceSlot);
        Assert.False(Assert.IsType<AuraSkinSlotRequest>(new AuraSkinSlotDecoder().Decode(GameMessageMetadata.Empty, [0x2A, 0x01])).Complete);
    }

    [Fact]
    public void Success_is_one_byte_and_refusal_carries_the_client_message_code()
    {
        Assert.Equal(new byte[] { 1 }, AuraSkinSlotPayloadCodec.Encode(new AuraSkinSlotResponse()));
        Assert.Equal(new byte[] { 0, 17 }, AuraSkinSlotPayloadCodec.Encode(new AuraSkinSlotResponse(AuraSkinSlotResponse.MissingTicket)));
        Assert.Equal(new byte[] { 0, 18 }, AuraSkinSlotPayloadCodec.Encode(new AuraSkinSlotResponse(AuraSkinSlotResponse.AlreadyOpen)));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void Subtype_one_carries_the_flag_at_seventeen_just_before_the_character_id(bool open, byte expected)
    {
        var source = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        var publicState = source.CharacterEntry.Public with
        {
            Extras = source.CharacterEntry.Public.Extras with { AuraSkinSlotOpen = open },
        };
        var core = StackableItemCore.Create(8, 42609, 1234);
        var body = StateSynchronizationPayloadCodec.EncodeExtendedUserState(source.Character,
            publicState, source.CharacterEntry.Skills,
            [new(new(987), 3, core, new(0, 321, new byte[30], 17, 29, 0))],
            new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(1, body[0]);
        Assert.Equal(expected, body[17]);
        Assert.All(body[5..17], b => Assert.Equal(0, b));
        Assert.Equal(checked((ushort)source.Character.Identity.CharacterId.Value), BitConverter.ToUInt16(body, 18));
    }
}
