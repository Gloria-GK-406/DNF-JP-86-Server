using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildEconomyRuleService(IGuildEconomySource content)
{
    public int? DonationGoldPerPoint() => content.ReadDonationGoldPerPoint() is > 0 and var rate ? rate : null;

    public int? CreationCost() => content.ReadGoldCost() is > 0 and var cost ? cost : null;
}
