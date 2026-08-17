using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;

namespace Dfo.Server.Protocol.Tests.Game.RosterScreenQueries;

/// <summary>
/// The dungeon-selection burst, byte for byte against the reference server's builders.
/// </summary>
/// <remarks>
/// These bodies are transcribed, not verified against this client: the client's own handlers for
/// these notification types were not walked. The tests pin what is being sent so a later capture
/// can be diffed against a known baseline rather than against whatever the code happened to do.
/// </remarks>
public sealed class DungeonSelectionCodecTests
{
    private static readonly EntrySpawnState Spawn =
        new(TownId: 3, AreaId: 7, X: 0x1234, Y: -2, Direction: 5, AreaState: 3, IsTransient: false);

    [Fact]
    public void EnterSelectDungeon_ReadsItsSelectionWord()
    {
        var request = (EnterSelectDungeonRequest)new EnterSelectDungeonDecoder()
            .Decode(GameMessageMetadata.Empty, Convert.FromHexString("10270000"))!;

        Assert.Equal((ushort)10000, request.Selection);
    }

    /// <summary>
    /// The selection stops at two bytes. Reading four would fold the paired 0x0010's difficulty and
    /// hell-party flags into the id, which the observed tutorial frame hides by leaving them zero.
    /// </summary>
    [Fact]
    public void EnterSelectDungeon_StopsAtTheWordEvenWhenTheNextBytesAreSet()
    {
        var request = (EnterSelectDungeonRequest)new EnterSelectDungeonDecoder()
            .Decode(GameMessageMetadata.Empty, Convert.FromHexString("10270101"))!;

        Assert.Equal((ushort)10000, request.Selection);
    }

    [Fact]
    public void EnterSelectDungeon_RefusesABodyTooShortForTheWord() =>
        Assert.Throws<InvalidDataException>(
            () => new EnterSelectDungeonDecoder().Decode(
                GameMessageMetadata.Empty, [0x10]));

    [Fact]
    public void UserArea_SubstitutesTheSelectionScreenAreaForTheRealOne()
    {
        var body = DungeonSelectionPayloadCodec.EncodeUserArea(
            0x0102, Spawn, DungeonSelectionPayloadCodec.SelectionScreenAreaId);

        // u16 user, u8 town, u8 area, i16 x, i16 y, u8 direction, u8 state
        Assert.Equal("020103FF3412FEFF0503", Convert.ToHexString(body));
        Assert.Equal(10, body.Length);
        Assert.Equal(0xFF, body[3]);
        Assert.NotEqual(Spawn.AreaId, body[3]);
    }

    [Fact]
    public void UserState_IsACountedListOfOne()
    {
        var body = DungeonSelectionPayloadCodec.EncodeUserState(0x0102, 0x01);

        Assert.Equal("01020101", Convert.ToHexString(body));
    }

    [Fact]
    public void UdpHostOffersNothing() =>
        Assert.Equal("00", Convert.ToHexString(DungeonSelectionPayloadCodec.EncodeUdpHostUnavailable()));

    [Fact]
    public void EnterSelectDungeonState_PutsTheTowerFloorAtOffsetFourteen()
    {
        var body = DungeonSelectionPayloadCodec.EncodeEnterSelectDungeon(0x0102, 9);

        Assert.Equal(19, body.Length);
        // The reference server documents the solo reader as taking this u16 from offset 14.
        Assert.Equal(9, BitConverter.ToUInt16(body, 14));

        // i32 1, u16 0, u8 count=1, [u16 user, u8 0], i32 0, u16 floor, three zero bytes.
        Assert.Equal("01000000000001020100000000000900000000", Convert.ToHexString(body));
    }
}
