using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.TownProjection;

namespace Dfo.Server.Protocol.Tests.Game.TownProjection;

/// <summary>
/// The two commands that move a published character: where it stands, and which area it walks into.
/// </summary>
public sealed class TownProjectionCodecTests
{
    private static byte[] Hex(string value) => Convert.FromHexString(value);

    /// <summary>
    /// The captured body, nine times identical because the character never left the arrival gate.
    /// The coordinates are the gate's own: SilverCrown declares <c>[gate] 474 234</c>.
    /// </summary>
    [Fact]
    public void SetUserPosition_ReadsTheCoordinatesTheClientReported()
    {
        var request = (SetUserPositionRequest)new SetUserPositionDecoder()
            .Decode(GameMessageMetadata.Empty, Hex("DA01EA00056800"))!;

        Assert.Equal(474, request.X);
        Assert.Equal(234, request.Y);
        Assert.Equal(5, request.Direction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void SetUserPosition_RefusesABodyTooShortForTheCoordinates(int length) =>
        Assert.Throws<InvalidDataException>(
            () => new SetUserPositionDecoder().Decode(
                GameMessageMetadata.Empty, new byte[length]));

    /// <summary>
    /// Negative coordinates have to survive: the arrival points read out of the archive include
    /// them, so treating these as unsigned would place a character off the map.
    /// </summary>
    [Fact]
    public void SetUserArea_ReadsTheDestinationTownAreaAndPosition()
    {
        var request = (SetUserAreaRequest)new SetUserAreaDecoder()
            .Decode(GameMessageMetadata.Empty, Hex("0203DA01FEFF00000000000000000000"))!;

        Assert.Equal(2, request.TownId);
        Assert.Equal(3, request.AreaId);
        Assert.Equal(474, request.X);
        Assert.Equal(-2, request.Y);
    }

    /// <summary>
    /// The client sends fifteen bytes and only the first six carry a known meaning. The decoder
    /// takes the six rather than demanding fifteen: a length the census got wrong has already
    /// rejected a real frame once on 0x0252, and the nine trailing bytes are read by nobody.
    /// </summary>
    [Fact]
    public void SetUserArea_AcceptsTheSixKnownBytesWithoutRequiringTheFullFifteen()
    {
        var request = (SetUserAreaRequest)new SetUserAreaDecoder()
            .Decode(GameMessageMetadata.Empty, Hex("010005000A00"))!;

        Assert.Equal(1, request.TownId);
        Assert.Equal(0, request.AreaId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void SetUserArea_RefusesABodyTooShortForTheDestination(int length) =>
        Assert.Throws<InvalidDataException>(
            () => new SetUserAreaDecoder().Decode(
                GameMessageMetadata.Empty, new byte[length]));
}
