using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// One-shot dungeon selection context owned by one exact selected-character connection generation.
/// </summary>
public interface IDungeonEntryIntentDomainService
{
    void Record(SelectedSessionIdentity session, DungeonEntryIntent intent);

    DungeonEntryIntent? Consume(SelectedSessionIdentity session);

    /// <summary>Returns a refused attempt to selection without overwriting a newer intent.</summary>
    void RestoreIfAbsent(SelectedSessionIdentity session, DungeonEntryIntent intent);

    bool ClearExact(SelectedSessionIdentity session);
}
