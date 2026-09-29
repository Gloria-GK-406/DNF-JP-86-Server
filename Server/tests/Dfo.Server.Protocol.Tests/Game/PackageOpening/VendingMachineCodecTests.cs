using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.VendingMachine;
namespace Dfo.Server.Protocol.Tests.Game.PackageOpening;
public sealed class VendingMachineCodecTests
{
    [Fact]
    public void Captured_request_reads_table_group_and_material_slot()
    {
        var r=Assert.IsType<VendingMachineRequest>(new VendingMachineDecoder().Decode(GameMessageMetadata.Empty,Convert.FromHexString("01000000010000008200")));
        Assert.Equal(1u,r.MachineId);Assert.Equal(1u,r.GroupId);Assert.Equal(130,r.Slot);
        Assert.Equal(new byte[]{0,22},VendingMachineEncoder.Encode(new(null,22)));
    }
    [Theory]
    [InlineData(0)] [InlineData(9)] [InlineData(11)]
    public void Wrong_length_is_not_a_valid_prefix(int length)=>Assert.Throws<InvalidDataException>(()=>new VendingMachineDecoder().Decode(GameMessageMetadata.Empty,new byte[length]));
}
