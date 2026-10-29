using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game;

public sealed class LostLandTimeLimitTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(100)]
    public void Fever_notification_is_one_absolute_byte(byte percent)
    {
        var message = new DungeonFeverGaugeMessage(percent);
        Assert.Equal(0x0264, message.Type);
        Assert.Equal(new[] { percent }, message.EncodeBody());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DungeonFeverGaugeMessage(101).EncodeBody());
    }

    [Fact]
    public void Start_notification_preserves_the_two_native_u32_operands()
    {
        var message = new DungeonClearTimeLimitMessage(600, 1200);
        Assert.Equal(0x025C, message.Type);
        Assert.Equal(Convert.FromHexString("58020000B0040000"), message.EncodeBody());
    }
}
