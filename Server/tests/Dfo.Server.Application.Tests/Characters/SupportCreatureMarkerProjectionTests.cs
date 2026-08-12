using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Characters;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class SupportCreatureMarkerProjectionTests
{
    private static readonly DateTimeOffset PreparedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public void StaticPvfLifetimeOverridesCreatureDetailAndStoredCoreFallbacks()
    {
        Assert.Equal(900u, SupportCreatureMarkerProjection.Resolve(
            new CreatureEquipmentLifetime(900, 259_200),
            1_700_000_700, 555, PreparedAt));
    }

    [Theory]
    [InlineData(1_700_000_700, 555, 700u)]
    [InlineData(0, 1_700_000_600, 600u)]
    public void EffectiveCreatureDetailUsesCoreExpiryThenAbsoluteMarker(
        int expireTime, int marker16, uint expected)
    {
        Assert.Equal(expected, SupportCreatureMarkerProjection.Resolve(
            new CreatureEquipmentLifetime(0, 259_200),
            expireTime, marker16, PreparedAt));
    }

    [Fact]
    public void UsablePeriodDetailFallbackOverridesStoredRelativeMarker()
    {
        Assert.Equal(259_200u, SupportCreatureMarkerProjection.Resolve(
            new CreatureEquipmentLifetime(0, 259_200),
            0, 555, PreparedAt));
    }

    [Fact]
    public void StoredRelativeMarkerIsTheFinalFallback()
    {
        Assert.Equal(555u, SupportCreatureMarkerProjection.Resolve(
            default, 0, 555, PreparedAt));
    }
}
