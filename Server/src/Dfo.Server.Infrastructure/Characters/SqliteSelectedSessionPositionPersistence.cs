using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Infrastructure.Characters;

/// <summary>
/// Defers the selection-only SQLite adapter until an actual published runtime is cleaned.
/// This keeps ordinary channel/login network startup independent of character schema.
/// </summary>
internal sealed class SqliteSelectedSessionPositionPersistence(string databasePath)
    : ISelectedSessionPositionPersistence
{
    public ValueTask PersistAsync(
        AccountId accountId, CharacterId characterId, CharacterPositionState position,
        CancellationToken cancellationToken) =>
        new SqliteCharacterEntryMutationRepository(databasePath).PersistPositionAsync(
            accountId, characterId, position, cancellationToken);
}
