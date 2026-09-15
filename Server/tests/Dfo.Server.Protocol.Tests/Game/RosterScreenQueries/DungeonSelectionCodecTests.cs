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

    /// <remarks>
    /// This pinned the older client's nineteen bytes, including a tower-of-despair floor the test
    /// read back from offset fourteen. A21's frame is 37 bytes and has no floor in it -- the two
    /// shapes line up at no offset at all, so there was nothing to migrate. What is pinned now is
    /// A21's, and against the reference session rather than against this encoder: for one user its
    /// frame has non-zero bytes at 10, 11 and 18 and nowhere else, which is the user count, the low
    /// byte of the user id, and the constant one.
    /// </remarks>
    [Fact]
    public void EnterSelectDungeonState_MatchesTheA21ReferenceFrameForOneUser()
    {
        var body = DungeonSelectionPayloadCodec.EncodeEnterSelectDungeon(1);

        Assert.Equal(37, body.Length);
        Assert.Equal(
            new[] { 10, 11, 18 },
            Enumerable.Range(0, body.Length).Where(index => body[index] != 0).ToArray());
        Assert.Equal(
            "00000000000000000000010100000000000001000000000000000000000000000000000000",
            Convert.ToHexString(body));
    }

    /// <summary>A user id above 255 reaches both of its bytes, which the all-zeros case cannot show.</summary>
    [Fact]
    public void EnterSelectDungeonState_CarriesTheWholeUserId()
    {
        var body = DungeonSelectionPayloadCodec.EncodeEnterSelectDungeon(0x0102);

        Assert.Equal(0x0102, BitConverter.ToUInt16(body, 11));
    }

    [Fact]
    public void EnterSelectDungeonState_CarriesEveryFrozenPartyUser()
    {
        var body = DungeonSelectionPayloadCodec.EncodeEnterSelectDungeon(
            new ushort[] { 0x0102, 0x0304 });

        Assert.Equal(40, body.Length);
        Assert.Equal(2, body[10]);
        Assert.Equal(0x0102, BitConverter.ToUInt16(body, 11));
        Assert.Equal(0x0304, BitConverter.ToUInt16(body, 14));
        Assert.Equal(1, BitConverter.ToUInt16(body, 21));
    }
}
