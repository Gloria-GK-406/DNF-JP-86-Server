using Dfo.Server.Networking.Runtime;

namespace Dfo.Server.Networking.Tests.Runtime;

public sealed class GameInboundFrameCounterTests
{
    [Fact]
    public void Unknown_opcode_frames_still_advance_counter_and_stale_sequence_does_not()
    {
        var counter = new GameInboundFrameCounter();
        Assert.Equal((1L, true), counter.Observe(7));
        Assert.Equal((2L, true), counter.Observe(8));
        Assert.Equal((3L, false), counter.Observe(7));
        Assert.Equal((4L, true), counter.Observe(9));
    }

    [Fact]
    public void Full_sixteen_bit_wrap_is_fresh()
    {
        var counter = new GameInboundFrameCounter();
        Assert.True(counter.Observe(65534).Fresh);
        Assert.True(counter.Observe(65535).Fresh);
        Assert.True(counter.Observe(0).Fresh);
        Assert.False(counter.Observe(65535).Fresh);
        Assert.True(counter.Observe(1).Fresh);
    }
}
