using Dfo.Server.GmWeb;

namespace Dfo.Server.Host.Tests;

public sealed class GmLoginLimiterTests
{
    [Fact]
    public async Task ConcurrentFailuresCannotLoseTheirReservations()
    {
        var clock = new GmTestClock(); var limiter = new GmLoginLimiter(clock);
        var entered = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            var allowed = limiter.TryBegin("source", "operator", out var complete); complete(false); return allowed;
        })));
        Assert.Equal(5, entered.Count(allowed => allowed));
        clock.Advance(TimeSpan.FromMinutes(15)); Assert.True(limiter.TryBegin("source", "operator", out _));
    }
    [Fact]
    public void CapacityIsBoundedAndNewSourcesCannotEvictBlockedSources()
    {
        var clock = new GmTestClock(); var limiter = new GmLoginLimiter(clock);
        for (var i = 0; i < 5; i++) { Assert.True(limiter.TryBegin("blocked", "operator", out var complete)); complete(false); }
        for (var i = 1; i < GmLoginLimiter.SourceCapacity; i++) Assert.True(limiter.TryBegin("source-" + i, "operator", out _));
        Assert.False(limiter.TryBegin("extra", "operator", out _)); Assert.False(limiter.TryBegin("blocked", "operator", out _));
        clock.Advance(TimeSpan.FromMinutes(15)); Assert.True(limiter.TryBegin("extra", "operator", out _));
    }
    [Fact]
    public void SuccessDoesNotGrowNameStorageBeyondCapacityOrEraseOtherFailures()
    {
        var limiter = new GmLoginLimiter(new GmTestClock());
        for (var i = 0; i < GmLoginLimiter.NameCapacity; i++) { Assert.True(limiter.TryBegin("source", "name-" + i, out var complete)); complete(true); complete(true); }
        Assert.False(limiter.TryBegin("source", "extra-name", out _));
        for (var i = 0; i < 5; i++) { Assert.True(limiter.TryBegin("source", "name-0", out var complete)); complete(false); }
        Assert.False(limiter.TryBegin("source", "name-0", out _));
    }
}
