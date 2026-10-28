using Dfo.Server.Abstractions.World;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfGuardianAltarContentTests
{
    [SkippableFact]
    public void Registered_stages_use_their_declared_maps_and_preserve_survival_order()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfGuardianAltarContentSource(provider);
        var content = source.Catalog;
        Assert.Same(content, source.Catalog);
        Assert.Equal(Enumerable.Range(3511, 13), content.Stages.Select(x => x.DungeonId));
        Assert.All(content.Stages.Take(12), stage =>
        {
            Assert.False(stage.Survival);
            Assert.Equal(3, stage.Maps.Length);
            Assert.Empty(stage.SurvivalMaps);
            Assert.All(stage.Maps, map => Assert.Contains(map.MapId, content.Maps.Keys));
        });
        var first = content.Stages[0];
        Assert.Equal(new[] { 25081, 25082, 25083 }, first.Maps.Select(x => x.MapId));
        Assert.Equal(new[] { 0, 10, 17 }, first.Maps.Select(x => x.BasisLevel));
        Assert.Contains(new GuardianAltarReward("normal", 2749211, 4), first.Maps[0].Rewards);
        var survival = content.Stages[^1];
        Assert.True(survival.Survival);
        Assert.Empty(survival.Maps);
        Assert.Equal(99, survival.SurvivalMaps.Length);
        Assert.Equal(new[] { 25129, 25122, 25124, 25128, 25123 }, survival.SurvivalMaps.Take(5));
        Assert.Equal(15, survival.SurvivalMaps.Distinct().Count());
        Assert.All(survival.SurvivalMaps, map => Assert.Contains(map, content.Maps.Keys));
        var rounds = survival.SurvivalRewards.SelectMany(x => Enumerable.Range(x.FirstRound, x.LastRound - x.FirstRound + 1));
        Assert.Equal(Enumerable.Range(1, 99), rounds);
        Assert.Equal(2, content.Maps[25081].Phases.Length);
        Assert.Equal(new[] { 125000, 20000 }, content.Maps[25081].Phases.Select(x => x.DurationMilliseconds));
        Assert.Equal(new GuardianAltarActionAssignment(1001, -1, -1), Assert.Single(content.Maps[25081].Phases[0].Assignments));
        Assert.Equal(64597, content.Maps[25081].CenterMonsterCode);
    }

    [SkippableFact]
    public void Units_skills_towers_gage_and_achievements_come_from_their_own_tables()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var content = new PvfGuardianAltarContentSource(provider).Catalog;
        Assert.Equal(3, content.Configuration.DailyRefillTickets);
        Assert.Equal((double)0.8f, content.Configuration.UpgradeCostPercent);
        Assert.Equal(new[] { 10, 30, 70, 150 }, content.Configuration.GageLevels.Select(x => x.Capacity));
        Assert.Equal(new[] { 1000, 1100, 1750, 2500 }, content.Configuration.GageLevels.Select(x => x.Rate));
        Assert.Equal(new GuardianAltarSummon(1, "enemy", "normal", 64599, 1, 1, 1682, 265, 0), content.Summons[1]);
        var unit = Assert.Single(content.Shop, x => x.Kind == GuardianAltarShopKind.Unit && x.Id == 1000 && x.RidableId == 1);
        Assert.Equal(0, unit.UnlockStageId);
        Assert.Equal(new[] { 0, 1, 2, 2 }, unit.Levels.Select(x => x.Price));
        Assert.Equal(new[] { 2749171, 2749172, 2749173, 2749174 }, unit.Levels.Select(x => x.ResourceId));
        Assert.All(unit.Levels, x => Assert.Equal(5, x.NeedPoint));
        var silverHook = Assert.Single(content.Shop, x => x.Kind == GuardianAltarShopKind.Unit && x.Id == 7000);
        Assert.Equal(new[] { 1, 2, 3, 3 }, silverHook.Levels.Select(x => x.Level));
        Assert.Equal(new[] { 2749201, 2749202, 2749203, 2749204 }, silverHook.Levels.Select(x => x.ResourceId));
        Assert.Contains(content.Shop, x => x.Kind == GuardianAltarShopKind.Skill && x.Id == 2001 && x.UnlockStageId == 3512);
        Assert.Contains(content.Shop, x => x.Kind == GuardianAltarShopKind.Tower && x.Id == 64597);
        Assert.Equal(500, content.StarResetGold[0]);
        Assert.Equal(30000, content.StarResetGold[10]);
        Assert.Contains(content.Achievements, x => x.Difficulty == GuardianAltarDifficulty.Easy && x.ClearCount == 4
            && x.Rewards.Contains(new GuardianAltarReward("normal", 2749213, 2)));
    }
}
