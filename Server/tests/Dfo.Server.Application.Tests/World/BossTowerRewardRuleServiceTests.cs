using Dfo.Server.Testing;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Application.World.BossTower;

namespace Dfo.Server.Application.Tests.World;

[Trait("Category", "Unit")]
public sealed class BossTowerRewardRuleServiceTests
{
    [Fact]
    public async Task Terminal_uses_current_stage_weight_and_supported_clear_bonuses_then_caps_final_total()
    {
        var rules = Rules();
        var level = 65;
        var mob = MonsterExperienceWorth.For(level, ExperienceTuning.Default.Curve);
        Assert.Equal((uint)(32*mob), await rules.ClearExperienceAsync(level,8,default));
        var bonuses = new Bonuses();
        var calculation = new ExperienceCalculationDomainService(null!,bonuses,bossTower:rules, tower: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRewardRuleService>());
        var reward = await calculation.CalculateAsync(new TowerClearExperienceContext(new(ExperienceEvent.TowerClear,new(1),new(1),3505),DungeonParticipationMode.BossTower,8),65,default);
        Assert.Equal(ExperienceEvent.DungeonClear,bonuses.LastEvent);
        Assert.Equal((uint)(32*mob),reward.Base);
        Assert.Equal((uint)(16*mob),reward.GrowthContract);
        Assert.Equal(reward.Base+reward.GrowthContract,reward.Total);
        bonuses.Rate=1000;
        Assert.Equal(1100000u,(await calculation.CalculateAsync(new TowerClearExperienceContext(new(ExperienceEvent.TowerClear,new(1),new(1),3505),DungeonParticipationMode.BossTower,8),65,default)).Total);
    }
    [Fact]
    public async Task Dedicated_kill_base_gets_supported_kill_bonus_once_and_candidates_keep_gold_multiplier()
    {
        var rules=Rules(new() {Probabilities=[1000,0,0],CardCount=10});
        var basis=await rules.KillExperienceAsync(65,3,default);
        var bonuses=new Bonuses();
        var calculation=new ExperienceCalculationDomainService(null!,bonuses,bossTower:rules, tower: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRewardRuleService>());
        var award=await calculation.CalculateAsync(new TowerKillExperienceContext(new(ExperienceEvent.TowerKill,new(1),new(1),3505),DungeonParticipationMode.BossTower,3),65,default);
        Assert.Equal(ExperienceEvent.MonsterKill,bonuses.LastEvent); Assert.Equal(basis,award.Base);
        Assert.Equal((uint)Math.Floor(basis*.5),award.GrowthContract);
        var candidates=await rules.CandidatesAsync(65,8,123,default);
        Assert.Equal(10,candidates.Length); Assert.All(candidates,x=>{Assert.Equal(0,x.ItemId);Assert.Equal(4224,x.DisplayValue);});
    }
    private static BossTowerRewardRuleService Rules(BossTowerDomainServiceTests.Rules? tower=null)
    { var content=new TowerRewardTestContent(); return new(tower??new(),content,ExperienceTuning.Default,content,content,content); }
    private sealed class Bonuses:IExperienceBonusDomainService
    {
        public ExperienceEvent? LastEvent{get;private set;}
        public double Rate{get;set;}=.5;
        public ValueTask<IReadOnlyList<ExperienceModifier>> CollectAsync(ExperienceOccasion occasion,CancellationToken ct)
        { LastEvent=occasion.Event;return ValueTask.FromResult<IReadOnlyList<ExperienceModifier>>([new("test",Rate,ExperienceBonusKind.GrowthContract)]); }
    }
}
