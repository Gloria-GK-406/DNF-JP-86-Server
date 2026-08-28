using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// What an account's entitlements add to an award, and for how long.
/// </summary>
/// <remarks>
/// The rates are policy, not content: every one of the fifty-two premium types in
/// <c>etc/(r)premiumlist.etc</c> declares an empty <c>[bonus]</c>, so nothing in the archive says
/// what a premium term is worth. An unconfigured server therefore adds nothing, and these tests
/// configure a rate to check the rule rather than to assert a number anyone measured.
/// </remarks>
public sealed class ExperienceBonusRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private static readonly ExperienceEntitlementPolicy Configured =
        new(new Dictionary<int, double> { [1] = 0.20 });

    [Fact]
    public void AConfiguredEntitlementAddsItsRateUnderItsOwnName()
    {
        var found = Collect(Configured, Premium(type: 1, expiresIn: TimeSpan.FromDays(30)));

        var modifier = Assert.Single(found);
        Assert.Equal(0.20, modifier.Rate);
        Assert.Contains("1", modifier.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntitlementWhoseTermHasEndedAddsNothing() =>
        Assert.Empty(Collect(Configured, Premium(type: 1, expiresIn: TimeSpan.FromDays(-1))));

    /// <summary>An unconfigured server adds nothing, whatever the account holds.</summary>
    /// <remarks>
    /// The default, and deliberately not a plausible number. A rate invented here would be
    /// indistinguishable from one read from somewhere, which is the confusion this subject has
    /// already cost a day to.
    /// </remarks>
    [Fact]
    public void AnUnconfiguredServerAddsNothing() =>
        Assert.Empty(Collect(
            ExperienceEntitlementPolicy.None, Premium(type: 1, expiresIn: TimeSpan.FromDays(30))));

    /// <summary>A type nobody configured adds nothing even when others are configured.</summary>
    [Fact]
    public void AnUnconfiguredTypeAddsNothing() =>
        Assert.Empty(Collect(Configured, Premium(type: 9, expiresIn: TimeSpan.FromDays(30))));

    /// <remarks>
    /// Zero and below are read as a row that carries no end rather than as one that ended in 1970.
    /// The account tables use them for exactly that, and treating them as expired would silently
    /// retire whatever an operator had configured.
    /// </remarks>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void AnEntitlementWithNoDeclaredEndIsStillHeld(long expiresAt)
    {
        var found = Collect(Configured, new AccountPremiumEntryState(1, expiresAt));

        Assert.Single(found);
    }

    /// <summary>Two held entitlements report two contributions, not one combined.</summary>
    /// <remarks>
    /// Reported separately because how they combine is not settled: every captured award had
    /// exactly one contribution in play, so adding separately-rounded amounts and compounding the
    /// rates agree on all of them. Keeping them apart leaves the choice to the caller and leaves
    /// the evidence for it visible.
    /// </remarks>
    [Fact]
    public void EachHeldEntitlementIsReportedOnItsOwn()
    {
        var policy = new ExperienceEntitlementPolicy(
            new Dictionary<int, double> { [1] = 0.20, [2] = 0.50 });

        var found = Collect(
            policy,
            Premium(type: 1, expiresIn: TimeSpan.FromDays(30)),
            Premium(type: 2, expiresIn: TimeSpan.FromDays(30)));

        Assert.Equal(2, found.Count);
        Assert.Equal([0.20, 0.50], found.Select(m => m.Rate).Order());
    }

    [Fact]
    public void AnAccountWithNoEntitlementsAddsNothing() =>
        Assert.Empty(Collect(Configured));

    /// <summary>
    /// The channel's rate reproduces the excess in every captured kill.
    /// </summary>
    /// <remarks>
    /// Each row is one of the four captured awards: what the rule says the kill was worth, and
    /// what actually reached the wire. The difference is the channel's, and 0.05 is the only rate
    /// that produces all four -- 3, 3, 6 and 7 -- under the same flooring.
    /// </remarks>
    [Theory]
    [InlineData(62u, 65u)]
    [InlineData(72u, 75u)]
    [InlineData(126u, 132u)]
    [InlineData(146u, 153u)]
    public void TheChannelRateReproducesEachCapturedAward(uint worth, uint captured)
    {
        var found = new List<ExperienceModifier>();
        ExperienceBonusRules.FromChannel(
            ExperienceEvent.MonsterKill, ExperienceChannelPolicy.Measured, found);

        var channel = Assert.Single(found);
        Assert.Equal(ExperienceBonusKind.Channel, channel.Kind);
        Assert.Equal(captured, worth + (uint)Math.Floor(worth * channel.Rate));
    }

    /// <summary>The channel pays a kill and says nothing about the other occasions.</summary>
    /// <remarks>
    /// Not a limitation to be lifted casually: the rate was measured against kills only, and the
    /// settlement at the end of a run is not implemented here at all. Paying it elsewhere would be
    /// carrying a measurement into a place the measurement says nothing about.
    /// </remarks>
    [Theory]
    [InlineData(ExperienceEvent.DungeonClear)]
    [InlineData(ExperienceEvent.QuestCompletion)]
    public void TheChannelAddsNothingToAnOccasionItWasNotMeasuredAgainst(ExperienceEvent occasion)
    {
        var found = new List<ExperienceModifier>();
        ExperienceBonusRules.FromChannel(occasion, ExperienceChannelPolicy.Measured, found);

        Assert.Empty(found);
    }

    /// <summary>A channel configured to add nothing adds nothing.</summary>
    [Fact]
    public void AChannelRateOfZeroAddsNothing()
    {
        var found = new List<ExperienceModifier>();
        ExperienceBonusRules.FromChannel(
            ExperienceEvent.MonsterKill, ExperienceChannelPolicy.None, found);

        Assert.Empty(found);
    }

    /// <summary>A premium term is paid but belongs to no line the frame can draw.</summary>
    /// <remarks>
    /// Pinned because the consequence is invisible otherwise: the client subtracts the lines it
    /// was sent from the gain, so an unlisted amount is drawn as part of the base. Marking it
    /// <see cref="ExperienceBonusKind.Unlisted"/> is the deliberate choice not to guess which of
    /// the frame's named lines a premium term belongs to.
    /// </remarks>
    [Fact]
    public void APremiumTermBelongsToNoLineTheFrameDraws()
    {
        var found = Collect(Configured, Premium(type: 1, expiresIn: TimeSpan.FromDays(30)));

        Assert.Equal(ExperienceBonusKind.Unlisted, Assert.Single(found).Kind);
    }

    private static AccountPremiumEntryState Premium(int type, TimeSpan expiresIn) =>
        new(type, Now.Add(expiresIn).ToUnixTimeSeconds());

    private static List<ExperienceModifier> Collect(
        ExperienceEntitlementPolicy policy, params AccountPremiumEntryState[] premiums)
    {
        var found = new List<ExperienceModifier>();
        ExperienceBonusRules.FromAccountEntitlements(
            premiums, Now, ExperienceEvent.MonsterKill, policy, found);
        return found;
    }
}
