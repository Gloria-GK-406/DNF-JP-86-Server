using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.ExpertJobs;

internal sealed class DisjointMachineRuleService(EquipmentDisjointRuleService equipmentRules)
{
    internal async ValueTask<(DisjointMachineRejection Rejection, DisjointMachinePlan? Plan)> ResolveAsync(
        DisjointMachineDefinition machine, ItemSnapshot item, int machineGrade, int endurance, bool selfService,
        int randomSeed, CancellationToken ct)
    {
        if (await equipmentRules.ExpertTargetAsync(item, ct).ConfigureAwait(false) is not { } target)
            return (DisjointMachineRejection.InvalidItem, null);
        var (equipment, grade, state) = target;
        if (!machine.Results.TryGetValue((machineGrade - 1, equipment.Rarity, state), out var material))
            return (state == EquipmentDisjointRuleService.UnpurifiedState
                ? DisjointMachineRejection.GradeTooLow : DisjointMachineRejection.InvalidItem, null);

        var random = new Random(randomSeed);
        var adjustedGold = Math.Max(1, Math.Floor(equipment.SellGold * 1.1));
        var rewards = new List<EquipmentDisjointReward>();
        if (material.ItemId > 0)
            rewards.Add(new(material.ItemId, PositiveCount(adjustedGold * material.Multiplier / machine.BaseDivisor)));

        var great = random.Next(10000) < Math.Clamp(material.GreatChance, 0, 100) * 100;
        var extra = Select(great ? machine.GreatResults : machine.AdditionalResults,
            great ? material.GreatTable : material.AdditionalTable, grade, random);
        if (extra is { ItemId: > 0 })
            rewards.Add(new(extra.ItemId, extra.Divisor > 0 ? PositiveCount(grade / extra.Divisor) : 1));

        await equipmentRules.AddEquipmentSoulsAsync(rewards, equipment, random, ct).ConfigureAwait(false);
        if (selfService && equipment.Rarity > 1 && random.Next(100) < machine.SelfServiceChance)
            rewards.Add(new(machine.SelfServiceItemId, machine.SelfServiceCount));
        if (rewards.Count == 0) return (DisjointMachineRejection.InvalidItem, null);

        var totalRewards = rewards.GroupBy(reward => reward.ItemId)
            .Select(group => new OpeningReward(group.Key, checked(group.Sum(reward => reward.Count)))).ToArray();
        var wear = ExpertJobEquipmentRules.Between(random, machine.MinimumWear, machine.MaximumWear);
        var experience = ExpertJobEquipmentRules.Between(random, machine.MinimumExperience, machine.MaximumExperience);
        return (DisjointMachineRejection.None, new(totalRewards, Math.Max(0, endurance - wear), experience));
    }

    internal (ExpertMachineMaintenanceRejection Rejection, (int Cost, int Endurance, int Grade)? Quote) QuoteMaintenance(
        ExpertJobDefinition definition, DisjointMachineDefinition machine, ExpertJobState state, bool upgrade,
        int? characterLevel, int gold)
    {
        var grade = state.DisjointMachineGrade;
        if (grade <= 0 || grade > machine.Repairs.Count)
            return (ExpertMachineMaintenanceRejection.InvalidState, null);

        var repair = machine.Repairs[grade - 1];
        if (!upgrade)
            return ExpertMachineRepair.Quote(repair, state.DisjointMachineEndurance, gold) is { } quote
                ? (ExpertMachineMaintenanceRejection.None, (quote.Cost, quote.Endurance, grade))
                : (ExpertMachineMaintenanceRejection.CannotRepair, null);

        var target = grade + 1;
        if (state.DisjointMachineEndurance != repair.MaximumEndurance || target > machine.Repairs.Count
            || definition.LevelAt(state.Experience) < target
            || !machine.UpgradeCosts.TryGetValue(target, out var cost) || cost <= 0
            || !machine.MinimumCharacterLevels.TryGetValue(target, out var minLevel))
            return (ExpertMachineMaintenanceRejection.CannotUpgrade, null);
        if (characterLevel is not { } level || level < minLevel)
            return (ExpertMachineMaintenanceRejection.CharacterLevelTooLow, null);
        if (gold < cost)
            return (ExpertMachineMaintenanceRejection.InsufficientGold, null);
        return (ExpertMachineMaintenanceRejection.None, (cost, machine.Repairs[target - 1].MaximumEndurance, target));
    }

    private static int PositiveCount(double value) => (int)Math.Clamp(Math.Floor(value), 1, int.MaxValue);

    private static DisjointMachineSelectionRule? Select(IReadOnlyList<DisjointMachineSelectionRule> choices,
        int table, int grade, Random random)
    {
        var roll = random.Next(10000);
        foreach (var rule in choices)
        {
            if (rule.Table != table || grade < rule.MinimumGrade || grade > rule.MaximumGrade) continue;
            roll -= Math.Clamp(rule.Weight, 0, 10000);
            if (roll < 0) return rule;
        }
        return null;
    }
}

internal sealed record DisjointMachinePlan(IReadOnlyList<OpeningReward> Rewards, int Endurance, int ExperienceGain);
