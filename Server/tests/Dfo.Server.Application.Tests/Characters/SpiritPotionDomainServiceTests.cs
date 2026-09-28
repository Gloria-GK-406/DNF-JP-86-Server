using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class SpiritPotionDomainServiceTests
{
    [Fact]
    public void Reapplication_replaces_rate_and_expiry_then_expires_on_read()
    {
        var clock = new AdvanceableTimeProvider(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero));
        var potions = new SpiritPotionDomainService(clock);
        var character = new CharacterId(7);

        potions.Apply(character, 1, TimeSpan.FromHours(2));
        clock.Advance(TimeSpan.FromMinutes(30));
        potions.Apply(character, 0.5, TimeSpan.FromMinutes(10));
        Assert.Equal(0.5, potions.GetBonusRate(character));

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(0, potions.GetBonusRate(character));
        potions.Clear(character);
        potions.Clear(character);
        Assert.Equal(0, potions.GetBonusRate(character));
    }
}
