using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>The spirit potion, as the online character runtime keeps it.</summary>
public sealed class SpiritPotionDomainServiceTests
{
    [Fact]
    public void Reapplication_replaces_rate_and_expiry_then_expires_on_read()
    {
        TestGameplay.Run(() =>
        {
            var clock = new AdvanceableTimeProvider(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero));
            var potions = new OnlineCharacterRuntimeDomainService(clock, new OnlineCharacterLocationDomainService());
            var character = new CharacterId(7);
            var session = SelectedSessionIdentity.Create(
                new AccountId(1), character, new NetworkConnectionId("Game:potion"), 2, 10011);
            potions.Begin(session, new CharacterPositionState(1, 0, 0, 0, 0), 100);

            potions.ApplySpiritPotion(session, 1, TimeSpan.FromHours(2));
            clock.Advance(TimeSpan.FromMinutes(30));
            potions.ApplySpiritPotion(session, 0.5, TimeSpan.FromMinutes(10));
            Assert.Equal(0.5, potions.SpiritPotionBonusRateOf(character));

            clock.Advance(TimeSpan.FromMinutes(10));
            Assert.Equal(0, potions.SpiritPotionBonusRateOf(character));
            Assert.True(potions.EndExact(session));
            Assert.False(potions.EndExact(session));
            Assert.Equal(0, potions.SpiritPotionBonusRateOf(character));
        });
    }
}
