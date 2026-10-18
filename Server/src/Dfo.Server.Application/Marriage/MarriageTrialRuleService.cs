using Dfo.Server.Abstractions.Marriage;

namespace Dfo.Server.Application.Marriage;

internal sealed class MarriageTrialRuleService(IMarriageDungeonContentSource dungeons) : IMarriageTrialRuleService
{
    public ValueTask<bool> IsTrialMapAsync(int dungeon, int map, CancellationToken ct) => dungeons.IsOrdinaryMapAsync(dungeon, map, ct);
}
