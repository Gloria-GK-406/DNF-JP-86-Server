using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Marriage;

namespace Dfo.Server.Application.Marriage;

internal sealed class MarriageChurchRuleService(IMarriageCeremonyContentSource ceremony,
    ITownDefinitionSource? towns = null) : IMarriageChurchRuleService
{
    private static bool IsChurch(TownAreaDefinition area) =>
        area.MapPath.Replace('\\', '/').EndsWith("church/church.map", StringComparison.OrdinalIgnoreCase);

    public async ValueTask<bool> IsChurchAsync(byte townId, byte areaId, CancellationToken ct) =>
        towns is not null && (await towns.GetAsync(townId, ct).ConfigureAwait(false))?.AreaAt(areaId) is { } area && IsChurch(area);

    public TimeSpan PreparationTime(int waitIndex)
    {
        var values = ceremony.Load().WaitDurations;
        if (waitIndex < 0 || waitIndex >= values.Count) throw new ArgumentOutOfRangeException(nameof(waitIndex));
        return TimeSpan.FromSeconds(values[waitIndex]);
    }

    public async ValueTask<IReadOnlyList<MarriageChurchLocation>> LocationsAsync(byte townId, CancellationToken ct)
    {
        // A21 RVA008936A0 and RVA0089460A recognize churches only in town2, area12..62.
        if (townId != 2 || towns is null || await towns.GetAsync(townId, ct).ConfigureAwait(false) is not { } town) return [];
        var result = new List<MarriageChurchLocation>();
        foreach (var area in town.Areas.Where(x => IsChurch(x) && x.AreaId is >= 12 and <= 62))
        {
            var geometry = await towns.GetMapGeometryAsync(area.MapPath, ct).ConfigureAwait(false);
            if (geometry?.WalkableAreas.FirstOrDefault() is not { Width: > 0, Height: > 0 } bounds) continue;
            result.Add(new(townId, checked((byte)area.AreaId),
                checked((short)bounds.CenterX), checked((short)bounds.CenterY)));
        }
        return result.OrderBy(x => x.AreaId).ToArray();
    }
}
