using Dfo.Server.Abstractions.Content;

using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class HellRewardOptions
{
    // Upstream uses 100. This is a server policy, not a recovered official baseDropRate.
    public int EquipmentRatePercent { get; init; } = 100;
    // Upstream fragment attempts by ordinary difficulty; A adds one.
    public IReadOnlyList<int> EpicPieceAttempts { get; init; } = [5, 6, 8, 9, 10];
}
