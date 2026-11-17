using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

// Identity 7 is the drop ratio slot of the 70 server (CItemDropRatioEvent); A21 is only known to list it.
internal sealed class DropRateEventDomainService(IEventPersistence store, IEventPeriodRuleService periods) : EntranceEventDomainService(store, periods)
{
    internal const int ActivityId = 7;
    public override int EventId => ActivityId;
    protected override string Key => "drop-rate";
    protected override EventConfiguration Initial => new(0, "initial", false, "爆率提升", "爆率提升",
        "活动期间地面掉落的抽取次数增加。", "", "", null, null);
    protected override EventInfo[] Project(EventConfiguration configuration)
    {
        var info = Detailed(configuration, "");
        if (Rate(configuration.DrawMultipliers) is not { } rate) return [info];
        return [info with { Title = $"{rate}爆率活动", ShortName = $"{rate}爆率", Description = $"爆率{rate}。{info.Description}" }];
    }

    /// <summary>How the multipliers read to players; null when nothing is raised.</summary>
    private static string? Rate(IReadOnlyDictionary<string, int>? configured)
    {
        var values = DropDrawEntries.All.Select(entry =>
            configured?.GetValueOrDefault(DropDrawEntries.KeyOf(entry), DropDrawMultipliers.Minimum) ?? DropDrawMultipliers.Minimum).ToArray();
        var highest = values.Max();
        if (highest == DropDrawMultipliers.Minimum) return null;
        return values.All(value => value == highest) ? $"{highest}倍" : $"最高{highest}倍";
    }

    protected override EventConfiguration Configure(EventConfiguration c, EventChange change)
    {
        if (change.DrawMultipliers is not { } values) throw new ArgumentException("请配置掉落倍率。");
        foreach (var (key, multiplier) in values)
            if (!DropDrawEntries.TryParse(key, out _) || !DropDrawMultipliers.IsValid(multiplier))
                throw new ArgumentException($"掉落倍率无效：{key}={multiplier}。");
        return c with { DrawMultipliers = values };
    }
}
