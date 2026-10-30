using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class VendingMachineRuleService(IVendingMachineDefinitionSource definitions) : IVendingMachineRuleService
{
    public VendingMachineDefinition Machine(int machineId, int groupId) => definitions.GetRequired(machineId, groupId);

    public long TicketCount(VendingMachineDefinition machine) => machine.Rewards[^1].CumulativeWeight;

    public OpeningReward RewardAt(VendingMachineDefinition machine, long ticket) => Select(machine, ticket);

    internal static OpeningReward Select(VendingMachineDefinition definition, long ticket)
    {
        if (ticket < 0 || ticket >= definition.Rewards[^1].CumulativeWeight)
            throw new InvalidOperationException("Out-of-range vending random ticket.");
        return definition.Rewards.First(r => ticket < r.CumulativeWeight).Reward;
    }
}
