using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// Selects disjoint rewards. Settlement is deliberately absent: another provider (a player
/// disjoint machine) can reuse the same domain transaction while applying a different rule set.
/// </summary>
internal sealed class EquipmentDisjointRuleService(IEquipmentDisjointDefinitionSource definitions)
    : IEquipmentDisjointRuleService
{
    public async ValueTask<EquipmentDisjointRuleResult> ResolveAsync(
        EquipmentDisjointCandidate equipment,
        EquipmentDisjointSettings settings,
        int randomSeed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(equipment);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Method != EquipmentDisjointMethod.SystemNpc)
            return new(EquipmentDisjointRejection.UnsupportedMethod);
        if (equipment.Core.EquipmentLockId != 0
            || (equipment.Core.AmplifyType & 0x80) != 0)
            return new(EquipmentDisjointRejection.CannotDisjoint);

        var definition = await definitions.GetEquipmentAsync(equipment.ItemId, cancellationToken)
            .ConfigureAwait(false);
        var table = await definitions.GetSystemTableAsync(cancellationToken).ConfigureAwait(false);
        if (definition is not { CanDisjoint: true } || table is null
            || definition.Rarity < 0 || table.CubeCreationBase <= 0
            || table.CubeCreationMultipliers.Count == 0)
            return new(EquipmentDisjointRejection.CannotDisjoint);

        var random = new Random(randomSeed);
        var rewards = new List<EquipmentDisjointReward>();
        Add(rewards, table.CubeItemId, CubeCount(definition, table));
        AddAdditional(rewards, definition, table, random);
        AddExpand(rewards, definition, table, random);
        return rewards.Count == 0
            ? new(EquipmentDisjointRejection.CannotDisjoint)
            : new(EquipmentDisjointRejection.None, rewards);
    }

    private static int CubeCount(
        EquipmentDisjointDefinition definition, EquipmentDisjointTable table)
    {
        var index = definition.Rarity < table.CubeCreationMultipliers.Count
            ? definition.Rarity : 0;
        var raw = Math.Floor(Math.Max(1, definition.SellGold)
            * table.CubeCreationMultipliers[index] / table.CubeCreationBase);
        return raw >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)raw);
    }

    private static void AddAdditional(List<EquipmentDisjointReward> rewards,
        EquipmentDisjointDefinition definition, EquipmentDisjointTable table, Random random)
    {
        if (definition.Rarity >= table.AdditionalResults.Count
            || definition.Rarity >= table.AdditionalRules.Count)
            return;
        var candidates = table.AdditionalResults[definition.Rarity];
        if (candidates.Count == 0)
            return;
        var selected = candidates.Count == 1 ? candidates[0] : candidates[random.Next(candidates.Count)];
        var rule = table.AdditionalRules[definition.Rarity];
        // This follows the reference implementation: the fourth table value is not a gate here;
        // it belongs to the table row but the ordinary additional result is always selected.
        var divisor = Percent(random) < ClampPercent(rule.GreatChancePercent)
            && rule.GreatCountDivisor > 0
                ? rule.GreatCountDivisor : rule.NormalCountDivisor;
        Add(rewards, selected, LevelCount(definition.MinimumLevel, divisor, random));
    }

    private static void AddExpand(List<EquipmentDisjointReward> rewards,
        EquipmentDisjointDefinition definition, EquipmentDisjointTable table, Random random)
    {
        var index = definition.Rarity == 6 ? 5
            : definition.Rarity is >= 0 and <= 4 ? definition.Rarity : -1;
        if (index < 0 || index >= table.ExpandRules.Count)
            return;
        var rule = table.ExpandRules[index];
        if (!rule.Enabled || rule.ItemId <= 0
            || Percent(random) >= ClampPercent(rule.NormalChancePercent))
            return;
        var count = LevelCount(definition.MinimumLevel, rule.LevelDivisor, random);
        if (Percent(random) < ClampPercent(rule.GreatChancePercent) && count < int.MaxValue)
            count++;
        Add(rewards, rule.ItemId, count);
    }

    private static int LevelCount(int level, double divisor, Random random)
    {
        if (!double.IsFinite(divisor) || divisor <= 0)
            return 1;
        var value = Math.Max(1, level) / divisor;
        var count = Math.Floor(value);
        if (random.NextDouble() < value - count)
            count++;
        return count >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)count);
    }

    private static double Percent(Random random) => random.NextDouble() * 100;

    private static double ClampPercent(double value) => Math.Clamp(value, 0, 100);

    private static void Add(List<EquipmentDisjointReward> rewards, int itemId, int count)
    {
        if (itemId <= 0 || count <= 0)
            return;
        var index = rewards.FindIndex(reward => reward.ItemId == itemId);
        if (index < 0)
            rewards.Add(new(itemId, count));
        else
            rewards[index] = rewards[index] with
            {
                Count = checked(rewards[index].Count + count),
            };
    }
}
