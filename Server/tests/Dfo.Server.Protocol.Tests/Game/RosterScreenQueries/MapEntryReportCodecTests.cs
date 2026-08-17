using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MapEntryReports;

namespace Dfo.Server.Protocol.Tests.Game.RosterScreenQueries;

/// <summary>
/// The five map-entry reports, each pinned by the body the client actually sent on 2026-08-17.
/// </summary>
public sealed class MapEntryReportCodecTests
{
    private static byte[] Hex(string value) => Convert.FromHexString(value);

    [Fact]
    public void SetUdpIpPort_KeepsTheWholeBodyBecauseItsStructureIsUnknown()
    {
        var body = Hex("05AC1EE00101E01EAC2062C00500000C000000333065336134343865643066");

        var request = (SetUdpIpPortReport)new SetUdpIpPortDecoder()
            .Decode(GameMessageMetadata.Empty, body)!;

        Assert.Equal(31, request.Body.Length);
        Assert.Equal(body, request.Body);
    }

    [Fact]
    public void InformNoticeSecond_ReadsItsSingleWord()
    {
        var request = (InformNoticeSecondReport)new InformNoticeSecondDecoder()
            .Decode(GameMessageMetadata.Empty, Hex("10000000"))!;

        Assert.Equal(16u, request.Value);
    }

    [Fact]
    public void Cera_CarriesNoBody()
    {
        Assert.NotNull(new CeraQueryDecoder().Decode(GameMessageMetadata.Empty, []));
        Assert.Throws<InvalidDataException>(
            () => new CeraQueryDecoder().Decode(GameMessageMetadata.Empty, [0x00]));
    }

    [Fact]
    public void SecurityStatus_AcceptsEightBytesEvenThoughTheCensusSaysTwelve()
    {
        // The static census reports "at least 12" for this type; the capture is eight. A decoder
        // built to the census would reject every real frame, so the sample sets the bound.
        var request = (SecurityStatusReport)new SecurityStatusDecoder()
            .Decode(GameMessageMetadata.Empty, Hex("0F0000007F000001"))!;

        Assert.Equal(8, request.Body.Length);
    }

    [Fact]
    public void RuntimeTingStatistic_ReadsItsSingleByte()
    {
        var request = (RuntimeTingStatisticReport)new RuntimeTingStatisticDecoder()
            .Decode(GameMessageMetadata.Empty, [0x00])!;

        Assert.Equal(0, request.Value);
    }
}
