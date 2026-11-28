using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Guilds;
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
    IOnlineCharacterRuntimeDomainService onlineCharacters,
    IAdventureGroupDomainService adventureGroups,
    IGuildBenefitDomainService benefits, IDungeonRunQueryDomainService runs) : IExperienceBonusDomainService
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
        var adventureRate = occasion.Event == ExperienceEvent.DungeonClear
            ? await adventureGroups.ClearExperienceRateAsync(occasion.Account, occasion.Character, cancellationToken)
                .ConfigureAwait(false)
            : 0;
        var channel = await channels.ResolveAsync(session.ListenerPort, cancellationToken).ConfigureAwait(false);
        var guildRate = occasion.Event == ExperienceEvent.MonsterKill && occasion.AllowGuildExperience
            ? await benefits.ReadMonsterExperienceRateAsync(occasion.Character, cancellationToken).ConfigureAwait(false)
            : 0;
        if (!sessions.IsInWorld(session))
            return [];

        var found = experienceRules.ModifiersFor(occasion.Event, currentPremiums,
            clock.GetUtcNow(), channel.ChannelId, occasion.DungeonId).ToList();
        if (occasion.Event is ExperienceEvent.MonsterKill or ExperienceEvent.DungeonClear && growthRate > 0)
            found.Add(new("growth contract", growthRate, ExperienceBonusKind.GrowthContract));
        if (guildRate > 0)
            found.Add(new("guild", guildRate, ExperienceBonusKind.GuildReward));
        if (blackDiamondRate > 0)
            found.Add(new("black diamond", blackDiamondRate, ExperienceBonusKind.BlackDiamond));
        // 70Reference: the timed EXP potion contributes to clear settlement, not ordinary kills.
        var potionRate = onlineCharacters.SpiritPotionBonusRateOf(occasion.Character);
        if (occasion.Event == ExperienceEvent.DungeonClear && potionRate > 0)
            found.Add(new("spirit potion", potionRate, ExperienceBonusKind.SpiritPotion));
        // Last, so a capped settlement (the boss tower) cuts it before the bonuses that were there
        // first.
        if (adventureRate > 0)
            found.Add(new("adventure group", adventureRate, ExperienceBonusKind.AdventureGroup));
        if (occasion.RunId is { } runId && runs.Find(occasion.Character) is
            { Layout.Mode.Kind: DungeonRunKind.Ordinary, Participant: { } participant } run && run.RunId == runId)
        {
            if (participant.IsLuckyCharacter)
                found.Add(new("lucky character", occasion.Event == ExperienceEvent.DungeonClear ? 0.15 : 0.10,
                    ExperienceBonusKind.LuckyCharacter));
            if (occasion.Event == ExperienceEvent.DungeonClear
                && run.Participants.Any(member => member.CharacterId != occasion.Character && member.IsLuckyCharacter
                    && run.ClearParticipants.Any(eligible => eligible.CharacterId == member.CharacterId)))
                found.Add(new("lucky character party", 0.10, ExperienceBonusKind.LuckyCharacterParty));
            if (participant.BurnsFatigue)
                found.Add(new("fatigue burning", 1.0, ExperienceBonusKind.FatigueBurn));
        }
        return found;
    }
}
