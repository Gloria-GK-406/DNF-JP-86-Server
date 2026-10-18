using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Application.Tests.World;

public sealed class SaoTowerPayloadTests
{
    private static byte[] Encode(SaoTowerProgress progress) => (byte[])typeof(Dfo.Server.Protocol.ProtocolModule).Assembly
        .GetType("Dfo.Server.Protocol.Game.SaoCurrentFloorEncoder")!
        .GetMethod("Body", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(null, [progress])!;
    [Fact]
    public void Round_and_next_floor_are_two_little_endian_u32_values()
    {
        Assert.Equal(new byte[] { 2, 0, 0, 0, 100, 0, 0, 0 }, Encode(new("season", 2, 100)));
        Assert.Equal(new byte[] { 3, 0, 0, 0, 1, 0, 0, 0 }, Encode(new("season", 3, 1)));
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => Encode(new("season", 1, 101)));
        Assert.IsType<InvalidDataException>(error.InnerException);
    }
}
