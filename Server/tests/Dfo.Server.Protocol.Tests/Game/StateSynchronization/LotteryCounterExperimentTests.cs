using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

public sealed class LotteryCounterExperimentTests
{
    [Theory]
    [InlineData(0, 30)]
    [InlineData(30, 30)]
    [InlineData(127, 127)]
    public void Counter_is_noti_0353_with_exactly_two_bytes(byte current, byte threshold)
    {
        var message = new LotteryCounterStateMessage(current, threshold);
        Assert.IsAssignableFrom<IGameNotiMessage>(message);
        Assert.Equal((ushort)0x0353, message.Type);
        Assert.Equal(new[] { current, threshold }, message.EncodeBody());
    }

    [Theory]
    [InlineData(128, 30)]
    [InlineData(0, 255)]
    public void Signed_client_values_are_not_silently_wrapped(byte current, byte threshold) =>
        Assert.Throws<InvalidDataException>(() => new LotteryCounterStateMessage(current, threshold).EncodeBody());
}
