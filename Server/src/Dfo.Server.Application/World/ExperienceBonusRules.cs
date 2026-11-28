using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.World;

// Legacy operator rates for other premiums. Growth contract is priced by its domain service.
public sealed record ExperienceEntitlementPolicy(IReadOnlyDictionary<int, double> RatesByPremiumType)
{
    public static ExperienceEntitlementPolicy None { get; } = new(new Dictionary<int, double>());
    public double RateFor(int premiumType) =>
        RatesByPremiumType.TryGetValue(premiumType, out var rate) && double.IsFinite(rate) && rate > 0 ? rate : 0;
}

internal static class ExperienceBonusRules
{
    public static void FromAccountEntitlements(IReadOnlyList<AccountPremiumEntryState> premiums, DateTimeOffset at,
        ExperienceEvent occasion, ExperienceEntitlementPolicy policy, ICollection<GameplayBenefit> into)
    {
        for (var index = 0; index < premiums.Count; index++)
        {
            var premium = premiums[index];
            if (premium.PremiumType == (int)PremiumServiceType.GrowthContract
                || premium.ExpiresAtUnixSeconds <= at.ToUnixTimeSeconds()) continue;
            var rate = policy.RateFor(premium.PremiumType);
            if (rate > 0)
                into.Add(DungeonExperienceBenefits.Entry($"legacy-{premium.PremiumType}", $"premium type {premium.PremiumType}",
                    index - premiums.Count, occasion, rate, ExperienceBonusKind.Unlisted,
                    DateTimeOffset.FromUnixTimeSeconds(premium.ExpiresAtUnixSeconds)));
        }
    }

    public static void FromChannel(ExperienceEvent occasion, double rate, ICollection<GameplayBenefit> into)
    {
        if (occasion == ExperienceEvent.MonsterKill && rate > 0)
            into.Add(DungeonExperienceBenefits.Entry("seria-blessing", "channel", 100, occasion, rate,
                ExperienceBonusKind.SeriaBlessing));
    }
}
