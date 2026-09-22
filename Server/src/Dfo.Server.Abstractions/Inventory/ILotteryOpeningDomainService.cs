using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface ILotteryResultSelector
{
    OpeningReward Select(OpeningPackageDefinition definition);
}
public sealed record LotteryOpeningResult(OpeningInventoryReceipt Receipt, int SourceSlot, int GoldAfter);
public interface ILotteryOpeningDomainService
{
    ValueTask<LotteryOpeningResult> ExecuteAsync(SelectedSessionIdentity session, int slot,
        ushort mode, ushort sequence, CancellationToken token);
}
