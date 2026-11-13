using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Which pots the archive declares, and the weighted draw from one.</summary>
public interface ILotteryRuleService
{
    /// <exception cref="InvalidDataException">The archive declares no such pot.</exception>
    void RequireDeclared(int potItemId);

    OpeningReward Roll(int potItemId);

    /// <summary>Empty slots required before drawing, from the quick bar and the pot stack alone.</summary>
    System.Collections.Immutable.ImmutableArray<OpeningRoomRequirement> RoomBeforeDrawing(bool quickBarFull, int potCount);
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
