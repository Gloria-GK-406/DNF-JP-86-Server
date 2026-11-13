using Dfo.Server.Application.PlayerPortal;
using Dfo.Server.Data.PlayerPortal;

namespace Dfo.Server.Application.Tests.PlayerPortal;

/// <summary>What a player may add, and the per-currency limits (#329, RQ-010, RQ-022).</summary>
public sealed class PlayerPortalCeraRuleTests
{
    private readonly PlayerPortalRuleService rules = new();

    [Theory]
    [InlineData("Cera", 1)]
    [InlineData("TokenCera", int.MaxValue)]
    public void Cera_and_token_cera_pass_with_a_positive_amount(string currency, int amount) =>
        Assert.Equal((currency, amount), rules.NormalizeCera(new(Guid.NewGuid(), currency, amount)));

    [Theory]
    [InlineData("HappyTokenCera", 1)]
    [InlineData("cera", 1)]
    [InlineData("Gold", 1)]
    [InlineData("", 1)]
    [InlineData("Cera", 0)]
    [InlineData("Cera", -1)]
    public void Other_currencies_and_amounts_are_invalid(string currency, int amount) =>
        Assert.Equal(PlayerPortalRefusal.Invalid, Assert.Throws<PlayerPortalException>(() => rules.NormalizeCera(new(Guid.NewGuid(), currency, amount))).Reason);

    [Fact]
    public void An_empty_request_id_is_invalid() =>
        Assert.Equal(PlayerPortalRefusal.Invalid, Assert.Throws<PlayerPortalException>(() => rules.NormalizeCera(new(Guid.Empty, "Cera", 1))).Reason);

    [Fact]
    public void The_payload_hash_binds_currency_and_amount()
    {
        Assert.Equal(rules.CeraPayloadHash("Cera", 10), rules.CeraPayloadHash("Cera", 10));
        Assert.NotEqual(rules.CeraPayloadHash("Cera", 10), rules.CeraPayloadHash("TokenCera", 10));
        Assert.NotEqual(rules.CeraPayloadHash("Cera", 10), rules.CeraPayloadHash("Cera", 11));
    }

    [Fact]
    public void Zero_limits_never_refuse() => rules.CheckCeraLimits(PlayerPortalLimits.None, "Cera", int.MaxValue, long.MaxValue / 2);

    [Theory]
    [InlineData("Cera", 1000, 500, true)]      // exactly the daily limit
    [InlineData("Cera", 1000, 501, false)]
    [InlineData("Cera", 1001, 0, false)]       // over the per-grant limit
    [InlineData("TokenCera", 50, 50, true)]
    [InlineData("TokenCera", 51, 0, false)]
    [InlineData("TokenCera", 50, 51, false)]
    public void Each_currency_is_judged_by_its_own_limits(string currency, int amount, long used, bool passes)
    {
        // Cera: 1000 per grant, 1500 a day; token cera: 50 per grant, 100 a day.
        var limits = new PlayerPortalLimits(1000, 1500, 50, 100, 0, 0);
        if (passes) rules.CheckCeraLimits(limits, currency, amount, used);
        else Assert.Equal(PlayerPortalRefusal.LimitExceeded, Assert.Throws<PlayerPortalException>(() => rules.CheckCeraLimits(limits, currency, amount, used)).Reason);
    }
}
