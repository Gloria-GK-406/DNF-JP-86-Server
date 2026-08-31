using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.World;

/// <summary>
/// The one reader of everything that decides an experience award.
/// </summary>
/// <remarks>
/// The three pure rule classes are called from here and nowhere else. That is the point of the
/// arrangement: what a kill is worth, what a clear is worth and what a term adds were each written
/// as a function of their inputs, and until now each caller fetched those inputs for itself. A
/// caller that fetched one of them differently was a caller that paid a different amount, which is
/// the class of defect this subject has already produced twice.
/// </remarks>
internal sealed class ExperienceRuleService(
    IExperienceParameterSource parameters,
    IDungeonRankGradeSource rankGrades,
    IDungeonExperienceProgressionPolicySource progression,
    IChannelExperienceSource channels,
    IQuestContentRuleService quests,
    ExperienceTuning tuning,
    ExperienceEntitlementPolicy entitlements) : IExperienceRuleService
{
    public uint WorthOfKill(in KillCircumstances kill) =>
        MonsterExperienceRules.WorthOf(kill, parameters.Load(), tuning);

    public bool IsMonster(byte declaredActorType) =>
        MonsterExperienceRules.IsMonster(declaredActorType);

    public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear)
    {
        var settings = parameters.Load();
        var clearBase = DungeonClearExperienceRules.BaseWorth(clear, quests.Parameters(), settings);
        return new ClearExperienceWorth(
            clearBase,
            DungeonClearExperienceRules.RankBonus(
                clearBase, clear.ClientRankPoint, rankGrades.Load(), settings));
    }

    public IReadOnlyList<ExperienceModifier> ModifiersFor(
        ExperienceEvent occasion,
        IReadOnlyList<AccountPremiumEntryState> premiums,
        DateTimeOffset now,
        int channelId,
        int dungeonId)
    {
        var found = new List<ExperienceModifier>();
        ExperienceBonusRules.FromAccountEntitlements(
            premiums, now, occasion, entitlements, found);
        ExperienceBonusRules.FromChannel(
            occasion, channels.Load().RateFor(channelId, dungeonId), found);
        return found;
    }

    public DungeonExperienceProgressionPolicy Progression() => progression.Load();
}
