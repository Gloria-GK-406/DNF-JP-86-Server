using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface ILotteryResultSelector
{
    OpeningReward Select(OpeningPackageDefinition definition);
}
public sealed record LotteryOpeningResult(bool Started, OpeningInventoryReceipt? Receipt = null,
    int SourceSlot = -1, int? GoldAfter = null);
public interface ILotteryOpeningDomainService
{
    ValueTask<LotteryOpeningResult> ExecuteAsync(SelectedSessionIdentity session, int? slot,
        bool start, CancellationToken token);
}
