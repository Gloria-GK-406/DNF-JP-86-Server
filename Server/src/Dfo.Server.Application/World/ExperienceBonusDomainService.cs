using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class ExperienceBonusDomainService(
    ISelectedSessionDomainService sessions,
    IPremiumServiceDomainService premiums,
    IChannelEntryDomainService channels,
    IExperienceRuleService experienceRules,
    TimeProvider clock,
    IOnlineCharacterRuntimeDomainService onlineCharacters) : IExperienceBonusDomainService
{
    public async ValueTask<IReadOnlyList<ExperienceModifier>> CollectAsync(
        ExperienceOccasion occasion, CancellationToken cancellationToken)
    {
        if (!sessions.TryGetInWorld(occasion.Character, out var session) || session.AccountId != occasion.Account)
            return [];

        var account = premiums.For(occasion.Account);
        var currentPremiums = await account.ReadAsync(cancellationToken).ConfigureAwait(false);
        var growthRate = await account.GetExperienceBonusRateAsync(cancellationToken).ConfigureAwait(false);
        var blackDiamondRate = occasion.Event == ExperienceEvent.DungeonClear
            ? await account.GetBlackDiamondExperienceBonusRateAsync(cancellationToken).ConfigureAwait(false) : 0;
        var channel = await channels.ResolveAsync(session.ListenerPort, cancellationToken).ConfigureAwait(false);
        if (!sessions.IsInWorld(session))
            return [];

        var found = experienceRules.ModifiersFor(occasion.Event, currentPremiums,
            clock.GetUtcNow(), channel.ChannelId, occasion.DungeonId).ToList();
        if (occasion.Event is ExperienceEvent.MonsterKill or ExperienceEvent.DungeonClear && growthRate > 0)
            found.Add(new("growth contract", growthRate, ExperienceBonusKind.GrowthContract));
        if (blackDiamondRate > 0)
            found.Add(new("black diamond", blackDiamondRate, ExperienceBonusKind.BlackDiamond));
        // 70Reference: the timed EXP potion contributes to clear settlement, not ordinary kills.
        var potionRate = onlineCharacters.SpiritPotionBonusRateOf(occasion.Character);
        if (occasion.Event == ExperienceEvent.DungeonClear && potionRate > 0)
            found.Add(new("spirit potion", potionRate, ExperienceBonusKind.SpiritPotion));
        return found;
    }
}
