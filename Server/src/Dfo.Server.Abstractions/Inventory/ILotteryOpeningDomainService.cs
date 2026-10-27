using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface ILotteryResultSelector
{
    OpeningReward Roll(int potItemId);
}
public sealed record LotteryOpeningResult(OpeningInventoryReceipt Receipt, BusinessSlot SourceSlot, int GoldAfter);
public interface ILotteryOpeningDomainService
{
    ValueTask<LotteryOpeningResult> ExecuteAsync(SelectedSessionIdentity session, BusinessSlot slot,
        ushort mode, IEffectFootprintRecorder footprint, CancellationToken token);
}

public interface ILotteryRandomSource
{
    long NextInt64(long exclusiveUpperBound);
}
