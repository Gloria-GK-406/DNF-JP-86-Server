using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
namespace Dfo.Server.Application.Tests.Support;

internal sealed class TestExperienceRules : IExperienceRuleService
{
    public Dfo.Server.Abstractions.World.CharacterExperienceScale CharacterScale() => new(Progression().LevelThresholds);
    public uint MobReward(int level) => Dfo.Server.Application.World.MonsterExperienceWorth.For(level, Dfo.Server.Abstractions.World.MonsterWorthCurve.Recovered);
    public Dfo.Server.Abstractions.Effects.ExperienceAwardDecision DecideAward(Dfo.Server.Data.Characters.CharacterProgressionState before,
        Dfo.Server.Abstractions.World.ExperienceBreakdown breakdown) => Dfo.Server.Application.World.ExperienceProgressionRules.Decide(before, breakdown, CharacterScale());
    public DungeonExperienceProgressionPolicy Progression() => new(Enumerable.Range(1, 85).Select(x => (uint)x * 100).ToArray(), 100000, 10000, 100);
    public uint WorthOfKill(in KillCircumstances kill) => throw new NotSupportedException();
    public bool IsMonster(byte actor) => throw new NotSupportedException();
    public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear) => throw new NotSupportedException();
    public IReadOnlyList<ExperienceModifier> ModifiersFor(ExperienceEvent occasion, IReadOnlyList<AccountPremiumEntryState> premiums, DateTimeOffset now, int channelId, int dungeonId) => throw new NotSupportedException();
}

internal sealed class TestProgressionSource : IDungeonExperienceProgressionPolicySource
{
    public DungeonExperienceProgressionPolicy Load() => new TestExperienceRules().Progression();
}
