using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildProfileRuleService(IGuildEconomySource content)
{
    public int? MarkPrice(byte mark) =>
        content.ReadMarkPricing() is { FirstPaidMark: >= 0 and <= GuildMarkPricing.MarkCount, PointCost: > 0 } pricing
            ? pricing.Price(mark) : null;

    public bool AllowsMedalAura(GuildMedalAura aura) =>
        aura == GuildMedalAura.None || content.ReadMedalAuraCatalog()?.Contains(aura) == true;
}
