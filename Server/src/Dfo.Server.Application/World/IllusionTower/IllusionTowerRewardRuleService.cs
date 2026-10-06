using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;

namespace Dfo.Server.Application.World.IllusionTower;

// 70Reference operands; current server worth curve and SplitMix64 are not original-server data/PRNG.
internal sealed class IllusionTowerRewardRuleService(IIllusionTowerRuleService tower,
    IExperienceParameterSource experience, ExperienceTuning tuning,
    IClearRewardRateSource clearRates, IGoldDropTableSource goldTables, IDropItemPoolSource pools)
    : IIllusionTowerRewardRuleService
{
    private static uint Truncate(double value) => checked((uint)Math.Max(0, Math.Truncate(value)));
    public async ValueTask<uint> KillExperienceAsync(int characterLevel, byte actorType, CancellationToken token, int dungeonId = 11001)
    {
        var definition = await tower.GetDefinitionAsync(token, dungeonId).ConfigureAwait(false);
        var basic = Truncate(MonsterExperienceWorth.For(characterLevel, tuning.Curve) / 2d
            * tuning.RankRate(MonsterRank.Ordinary) * experience.Load().DifficultyRate(0));
        var actor = actorType is >= 5 and <= 8 ? Truncate(basic * definition.Rewards.ApcExperienceWeight) : basic;
        return Truncate(actor * definition.Rewards.MonsterExperienceWeight);
    }
    public async ValueTask<uint> ClearExperienceAsync(int characterLevel, int rewardLevel, CancellationToken token, int dungeonId = 11001)
    {
        var definition = await tower.GetDefinitionAsync(token, dungeonId).ConfigureAwait(false);
        var weight = definition.Rewards.ExperienceWeights[rewardLevel - 1];
        return Truncate(Math.Min(1_100_000, rewardLevel * (double)MonsterExperienceWorth.For(characterLevel, tuning.Curve) * weight));
    }
    public async ValueTask<ImmutableArray<TowerRewardCandidate>> CandidatesAsync(int characterLevel,
        int rewardLevel, ulong seed, CancellationToken token, int dungeonId = 11001)
    {
        var definition = await tower.GetDefinitionAsync(token, dungeonId).ConfigureAwait(false);
        var count = Math.Min(10, Math.Min(definition.Parameters.MaxClearItemCount, definition.Rewards.CardCounts[rewardLevel - 1]));
        var table = clearRates.Load(DropRateTables.ClearReward);
        var random = new DropRandom(seed);
        var output = ImmutableArray.CreateBuilder<TowerRewardCandidate>(count);
        var p = Math.Min(10, rewardLevel);
        var goldThreshold = definition.Rewards.ItemProbabilityOperands[0] * p;
        var itemThreshold = (definition.Rewards.ItemProbabilityOperands[0] + definition.Rewards.ItemProbabilityOperands[1]) * p;
        for (var i = 0; i < count; i++)
        {
            // Inclusive local 0..1000. Native generator's exceptional upper endpoint is known,
            // its distribution and sequence are not reproduced.
            var roll = random.Next(1001) - 1;
            if (roll < goldThreshold)
                output.Add(new(0, Gold(characterLevel, definition.Rewards.GoldWeight, table, random)));
            else if (roll < itemThreshold)
                output.Add(Item(characterLevel, table, random));
            else output.Add(new(-1, 0));
        }
        return output.MoveToImmutable();
    }
    private int Gold(int level, double towerWeight, ClearRewardTable table, DropRandom random)
    {
        var gold = goldTables.Load(DropRateTables.ClearReward);
        var ranks = experience.Load().ClearRankRates;
        if (ranks.Count < 2 || table.PartyMemberRate(1) is not { } party || table.DifficultyGoldRate(0) is not { } difficulty)
            throw new InvalidDataException("Tower gold content is incomplete.");
        var w = party * difficulty * (1 + ranks[1]);
        var a = Truncate(gold.BaseFor(level) * 175d / 1000);
        var b = Truncate(7d * a / 2);
        var c = Truncate(b * w);
        var d = Truncate(c / 2d * w);
        var variance = gold.VarianceFor(level);
        var offset = random.Next(checked(2 * variance + 1)) - 1 - variance;
        var native = Truncate(d * (100d + offset) / 100);
        return checked((int)Truncate(native * towerWeight));
    }
    private TowerRewardCandidate Item(int level, ClearRewardTable table, DropRandom random)
    {
        var empty = new TowerRewardCandidate(-1, 0);
        if (table.DropKindRates.Count == 0 || random.Next(10000) > table.DropKindRates[0]) return empty;
        if (table.ProfileNamed(ClearRewardProfiles.Default)?.BandFor(level) is not { } band
            || table.DifficultyRate(0) is not { } difficulty
            || random.Next(10000) > (long)band.Rate + difficulty) return empty;
        var typeRoll = random.Next(10000);
        var type = 0;
        for (; type < table.ItemTypeRates.Count; type++)
        {
            typeRoll -= table.ItemTypeRates[type];
            if (typeRoll <= 0) break;
        }
        // Avatar is a separate branch; no avatar pool is currently restored.
        if (type != 1 || table.Rarity is not { } rarity || table.GradeBandFor(level) is not { } grades) return empty;
        var quality = (DropRarity)rarity.BandOf(random.Next(1_000_001) - 1);
        return DropRules.Pick(pools.Load(DropItemFamily.Equipment), grades, quality, random) is { } item
            ? new(item.ItemId, 0, item.Durability) : empty;
    }
}
