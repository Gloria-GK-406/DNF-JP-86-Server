using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// Values base experience, rank and progression from archive and tuning.
/// </summary>
internal sealed class ExperienceRuleService(
    IExperienceParameterSource parameters,
    IDungeonRankGradeSource rankGrades,
    IDungeonExperienceProgressionPolicySource progression,
    IChannelExperienceSource channels,
    ExperienceTuning tuning) : IExperienceRuleService, Dfo.Server.Abstractions.Content.IContentWarmup
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

    public string Name => "experience-rules";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        _ = parameters.Load();
        _ = rankGrades.Load();
        _ = channels.Load();
        return ValueTask.FromResult(Progression().LevelThresholds.Count);
    }
}
