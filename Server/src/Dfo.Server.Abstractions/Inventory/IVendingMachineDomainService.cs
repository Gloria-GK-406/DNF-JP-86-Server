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
public sealed record VendingMachineResult(OpeningInventoryReceipt Receipt, BusinessSlot SourceSlot, int Remaining,
    OpeningReward Reward);
public interface IVendingMachineDomainService
{
    ValueTask<VendingMachineResult> UseAsync(SelectedSessionIdentity session, int machineId, int groupId,
        BusinessSlot slot, ushort sequence, CancellationToken cancellationToken);
}
