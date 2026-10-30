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
/// caller that fetched one of them differently was a caller that paid a different amount.
/// </remarks>
internal sealed class ExperienceRuleService(
    IExperienceParameterSource parameters,
    IDungeonRankGradeSource rankGrades,
    IDungeonExperienceProgressionPolicySource progression,
    IChannelExperienceSource channels,
    ExperienceTuning tuning,
    ExperienceEntitlementPolicy entitlements) : IExperienceRuleService, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private readonly Lazy<DungeonExperienceProgressionPolicy> policy = new(progression.Load);
    private CharacterExperienceScale? scale;
    public CharacterExperienceScale CharacterScale() => scale ??= new(Progression().LevelThresholds);

    public Dfo.Server.Abstractions.Effects.ExperienceAwardDecision DecideAward(
        Dfo.Server.Data.Characters.CharacterProgressionState before, ExperienceBreakdown breakdown) =>
        ExperienceProgressionRules.Decide(before, breakdown, CharacterScale());

    public uint WorthOfKill(in KillCircumstances kill) =>
        MonsterExperienceRules.WorthOf(kill, parameters.Load(), tuning);
    public uint MobReward(int level) => MonsterExperienceWorth.For(level, tuning.Curve);

    public bool IsMonster(byte declaredActorType) =>
        MonsterExperienceRules.IsMonster(declaredActorType);

    public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear)
    {
        var settings = parameters.Load();
        var clearBase = DungeonClearExperienceRules.BaseWorth(clear, tuning, settings);
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

    public DungeonExperienceProgressionPolicy Progression() => policy.Value;

    public string Name => "experience-rules";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        _ = parameters.Load();
        _ = rankGrades.Load();
        _ = channels.Load();
        return ValueTask.FromResult(Progression().LevelThresholds.Count);
    }
}
