using Dfo.Server.Abstractions.Effects;
using System.Collections.Immutable;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record VendingReward(OpeningReward Reward, long CumulativeWeight, bool Announce);
public sealed record VendingMachineDefinition(int MachineId, int GroupId, int MaterialId, int MaterialCount,
    ImmutableArray<VendingReward> Rewards);
public interface IVendingMachineDefinitionSource
{
    int Initialize();
    VendingMachineDefinition GetRequired(int machineId, int groupId);
}
/// <summary>What a vending machine group costs and what one draw from it pays.</summary>
public interface IVendingMachineRuleService
{
    /// <exception cref="InvalidDataException">The archive declares no such machine group.</exception>
    VendingMachineDefinition Machine(int machineId, int groupId);

    /// <summary>How many tickets one draw is taken from; a draw is a ticket in <c>[0, count)</c>.</summary>
    long TicketCount(VendingMachineDefinition machine);

    /// <summary>The reward a ticket lands on, by cumulative weight.</summary>
    OpeningReward RewardAt(VendingMachineDefinition machine, long ticket);
}
public sealed record VendingMachineResult(OpeningInventoryReceipt Receipt, BusinessSlot SourceSlot, int Remaining,
    OpeningReward Reward);
public interface IVendingMachineDomainService
{
    ValueTask<VendingMachineResult> UseAsync(SelectedSessionIdentity session, int machineId, int groupId,
        BusinessSlot slot, IEffectFootprintRecorder footprint, CancellationToken cancellationToken);
}
