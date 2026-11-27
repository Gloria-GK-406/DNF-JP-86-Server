using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildEconomyRuleService(IGuildEconomySource content)
{
    private const int MaximumDonationGold = 10_000_000;

    public (GuildMutationStatus Status, int Points) EvaluateDonation(int amount)
    {
        if (amount <= 0)
            return (GuildMutationStatus.InvalidDonationAmount, 0);
        if (amount > MaximumDonationGold)
            return (GuildMutationStatus.DonationTooLarge, 0);
        if (content.ReadDonationGoldPerPoint() is not (> 0 and var rate))
            return (GuildMutationStatus.DonationUnavailable, 0);
        if (amount % rate != 0)
            return (GuildMutationStatus.InvalidDonationAmount, 0);
        return (GuildMutationStatus.Success, amount / rate);
    }

    public int? CreationCost() => content.ReadGoldCost() is > 0 and var cost ? cost : null;
}
