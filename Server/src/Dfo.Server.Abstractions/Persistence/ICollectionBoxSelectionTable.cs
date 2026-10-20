using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Persistence;

public interface ICollectionBoxSelectionTable
{
    ValueTask<int?> ReadAsync(CharacterId character, CancellationToken ct);
    ValueTask WriteAsync(CharacterId character, byte boxId, CancellationToken ct);
}
