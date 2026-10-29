using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.World.IllusionTower;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Testing;
using Dfo.Server.Application.World;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.World;

public sealed class IllusionTowerRewardRuleServiceTests
{
    private static IllusionTowerRewardRuleService Service(IllusionTowerDomainServiceTests.Rules? tower = null,
        TowerRewardTestContent? content = null)
    {
        content ??= new();
        return new(tower ?? new(), content, ExperienceTuning.Default, content, content, content);
    }
    [PvfFact]
    public async Task Current_archive_drives_tower_experience_and_deterministic_candidates()
    {
        using var provider = ContentArchive.CreateContentProvider();
        var tower = new IllusionTowerRuleService(provider.GetRequiredService<IIllusionTowerContentSource>());
        var rules = new IllusionTowerRewardRuleService(tower, provider.GetRequiredService<IExperienceParameterSource>(),
            ExperienceTuning.Default, provider.GetRequiredService<IClearRewardRateSource>(),
            provider.GetRequiredService<IGoldDropTableSource>(), provider.GetRequiredService<IDropItemPoolSource>());
        Assert.Equal(692u, await rules.KillExperienceAsync(50, 0, default, 11001));
        Assert.Equal(2076u, await rules.KillExperienceAsync(50, 5, default, 11001));
        Assert.Equal(5065u, await rules.ClearExperienceAsync(50, 1, default, 11001));
        var found = new List<TowerRewardCandidate>();
        for (ulong seed = 0; seed < 16; seed++)
        {
            var candidates = await rules.CandidatesAsync(50, 15, seed, default, 11001);
            Assert.Equal(4, candidates.Length);
            Assert.Equal(candidates.ToArray(), (await rules.CandidatesAsync(50, 15, seed, default, 11001)).ToArray());
            found.AddRange(candidates);
        }
        Assert.Contains(found, x => x.ItemId == 0 && x.DisplayValue > 0);
        Assert.Contains(found, x => x.ItemId > 0 && x.Durability > 0);
    }

    [Theory]
    [InlineData(50, 4, 692)]
    [InlineData(50, 5, 2076)]
    [InlineData(40, 5, 1431)]
    public async Task Immediate_experience_uses_member_level_normal_rates_and_actor_multiplier(int level, byte actor, uint expected)
        => Assert.Equal(expected, await Service().KillExperienceAsync(level, actor, default, 11001));

    [Fact]
    public async Task Clear_uses_reward_floor_and_clips_final_earned_value()
    {
        var tower = new IllusionTowerDomainServiceTests.Rules { ExperienceWeight = 2.5 };
        Assert.Equal(10660u, await Service(tower).ClearExperienceAsync(50, 4, default, 11001));
        tower.ExperienceWeight = 100000;
        Assert.Equal(1100000u, await Service(tower).ClearExperienceAsync(50, 15, default, 11001));
    }
    [Fact]
    public async Task Candidate_count_empty_positions_and_gold_truncations_are_stable()
    {
        var tower = new IllusionTowerDomainServiceTests.Rules { CardCount = 30, Probabilities = [1001, 0, 100] };
        var rules = Service(tower);
        var result = await rules.CandidatesAsync(50, 1, 123, default, 11001);
        Assert.Equal(4, result.Length);
        // G1000 => A175 => B612 => C686 => D384 => native384 => tower4224.
        Assert.All(result, x => { Assert.Equal(0, x.ItemId); Assert.Equal(4224, x.DisplayValue); });
        Assert.Equal(result.ToArray(), (await rules.CandidatesAsync(50, 1, 123, default, 11001)).ToArray());
        tower.Probabilities = [0, 0, 100];
        Assert.All(await rules.CandidatesAsync(50, 1, 123, default, 11001), x => Assert.Equal(new(-1, 0), x));
    }
    [Fact]
    public async Task Equipment_and_avatar_remain_distinct_and_follow_level_profile()
    {
        var tower = new IllusionTowerDomainServiceTests.Rules { Probabilities = [0, 1001, 100] };
        var content = new TowerRewardTestContent();
        content.Table = content.Table with { ItemTypeRates = [0, 10000, 0, 0] };
        var service = Service(tower, content);
        var equipment = await service.CandidatesAsync(50, 1, 123, default, 11001);
        Assert.All(equipment, x => { Assert.Equal(3000, x.ItemId); Assert.Equal(0, x.DisplayValue); Assert.Equal(35, x.Durability); });
        content.Table = content.Table with { ItemTypeRates = [0, 0, 0, 10000] };
        Assert.All(await service.CandidatesAsync(50, 1, 123, default, 11001), x => Assert.Equal(-1, x.ItemId));
        content.Table = content.Table with { ItemTypeRates = [0, 10000, 0, 0], Profiles = [new("default", [new(1, 40, 10000), new(41, 200, 0)])] };
        Assert.All(await service.CandidatesAsync(50, 1, 123, default, 11001), x => Assert.Equal(-1, x.ItemId));
        Assert.All(await service.CandidatesAsync(40, 1, 123, default, 11001), x => Assert.Equal(3000, x.ItemId));
        content.Table = content.Table with { DifficultyRates = [10000] };
        Assert.All(await service.CandidatesAsync(50, 1, 123, default, 11001), x => Assert.Equal(3000, x.ItemId));
    }
}
