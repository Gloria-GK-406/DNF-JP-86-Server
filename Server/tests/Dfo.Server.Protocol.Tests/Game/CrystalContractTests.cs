using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CrystalContract;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game;

public sealed class CrystalContractTests
{
    [Theory]
    [InlineData("0005",0,5)] [InlineData("0103",1,3)] [InlineData("00FF",0,255)]
    public void Selection_request_preserves_both_bytes(string hex, byte mode, byte selection)
    {
        var request=Assert.IsType<CrystalContractRequest>(new CrystalContractDecoder().Decode(GameMessageMetadata.Empty,Convert.FromHexString(hex)));
        Assert.Equal(mode,request.Mode);Assert.Equal(selection,request.Selection);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void Truncated_or_extended_request_is_rejected(int length) =>
        Assert.Throws<InvalidDataException>(()=>new CrystalContractDecoder().Decode(GameMessageMetadata.Empty,new byte[length]));

    [Fact]
    public void Success_contains_result_and_two_handler_reads()
    {
        IServerMessageEncoder encoder=new CrystalContractEncoder();
        var frame=Assert.Single(encoder.Encode(new CrystalContractResponse(true,0,5)));
        Assert.Equal(Convert.FromHexString("010005"),frame[15..]);
        Assert.Equal(new byte[]{0,5},new CubeProgressionStateMessage(0,5).EncodeBody());
    }
}
