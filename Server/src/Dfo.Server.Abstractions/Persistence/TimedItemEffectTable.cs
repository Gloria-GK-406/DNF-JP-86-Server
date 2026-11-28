using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Persistence;

public sealed record TimedItemEffectRow(int ItemId, DateTimeOffset ExpiresAt);

public interface ITimedItemEffectTable
{
    ValueTask<IReadOnlyList<TimedItemEffectRow>> ReadActiveAsync(CharacterId character, DateTimeOffset at, CancellationToken ct);
    ValueTask<bool> TryStartAsync(CharacterId character, TimedItemEffectRow effect, DateTimeOffset at, CancellationToken ct);
}
