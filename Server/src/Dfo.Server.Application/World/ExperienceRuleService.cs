using System.Collections.Immutable;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.World;

/// <summary>
/// Values base experience, rank and progression from archive and tuning.
/// </summary>
internal sealed class ExperienceRuleService(
    IExperienceParameterSource parameters,
    IDungeonRankGradeSource rankGrades,
    IDungeonExperienceProgressionPolicySource progression,
    IChannelExperienceSource channels,
    ExperienceTuning tuning, ExperienceEntitlementPolicy entitlements, IPremiumServiceRuleService premiumRules)
    : IExperienceRuleService, Dfo.Server.Abstractions.Content.IContentWarmup
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

    public DungeonExperienceProgressionPolicy Progression() => policy.Value;

    internal ImmutableArray<GameplayBenefit> ChannelBenefits(ExperienceEvent occasion, int channelId, int dungeonId)
    {
        var found = ImmutableArray.CreateBuilder<GameplayBenefit>();
        ExperienceBonusRules.FromChannel(occasion, channels.Load().RateFor(channelId, dungeonId), found);
        return found.ToImmutable();
    }

    internal ImmutableArray<GameplayBenefit> PremiumBenefits(IReadOnlyList<AccountPremiumEntryState> entries,
        DateTimeOffset at, ExperienceEvent occasion, double blackDiamond)
    {
        var found = ImmutableArray.CreateBuilder<GameplayBenefit>();
        ExperienceBonusRules.FromAccountEntitlements(entries, at, occasion, entitlements, found);
        var growth = entries.SingleOrDefault(item => item.PremiumType == (int)PremiumServiceType.GrowthContract);
        if (occasion is ExperienceEvent.MonsterKill or ExperienceEvent.DungeonClear
            && growth is { } term && term.ExpiresAtUnixSeconds > at.ToUnixTimeSeconds()
            && premiumRules.GrowthExperienceBonusRate is > 0 and var growthRate)
            found.Add(DungeonExperienceBenefits.Entry("growth-contract", "growth contract", 200, occasion,
                growthRate, ExperienceBonusKind.GrowthContract, DateTimeOffset.FromUnixTimeSeconds(term.ExpiresAtUnixSeconds)));
        if (occasion == ExperienceEvent.DungeonClear && blackDiamond > 0)
            found.Add(DungeonExperienceBenefits.Entry("black-diamond", "black diamond", 400, occasion,
                blackDiamond, ExperienceBonusKind.BlackDiamond, basis: ExperienceBonusBasis.BaseAndClearRank));
        return found.ToImmutable();
    }

    public string Name => "experience-rules";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        _ = parameters.Load();
        _ = rankGrades.Load();
        _ = channels.Load();
        return ValueTask.FromResult(Progression().LevelThresholds.Count);
    }
}
