using Dfo.Server.Abstractions.Accounts;
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
    public static void FromAccountEntitlements(
        IReadOnlyList<AccountPremiumEntryState> premiums,
        DateTimeOffset now,
        ExperienceEvent occasion,
        ExperienceEntitlementPolicy policy,
        ICollection<ExperienceModifier> into)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(into);
        _ = occasion;
        foreach (var premium in premiums)
        {
            if (premium.PremiumType == (int)PremiumServiceType.GrowthContract
                || premium.ExpiresAtUnixSeconds <= now.ToUnixTimeSeconds()) continue;
            var rate = policy.RateFor(premium.PremiumType);
            if (rate > 0)
                into.Add(new($"premium type {premium.PremiumType}", rate, ExperienceBonusKind.Unlisted));
        }
    }

    public static void FromChannel(ExperienceEvent occasion, double rate, ICollection<ExperienceModifier> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (occasion == ExperienceEvent.MonsterKill && rate > 0)
            into.Add(new("channel", rate, ExperienceBonusKind.SeriaBlessing));
    }
}
