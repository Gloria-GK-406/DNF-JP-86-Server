using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// One-shot dungeon selection context owned by one exact selected-character connection generation.
/// </summary>
public interface IDungeonEntryIntentDomainService
{
    void Record(SelectedSessionIdentity session, DungeonEntryIntent intent);

    DungeonEntryIntent? Find(SelectedSessionIdentity session) => null;

    /// <summary>The session's intent, when it allows entering this dungeon; nothing is consumed.</summary>
    DungeonEntryIntent? Authorize(SelectedSessionIdentity session, int dungeonId, bool mirror);

    bool TryReplaceExact(SelectedSessionIdentity session, DungeonEntryIntent expected, DungeonEntryIntent replacement) => false;

    DungeonEntryIntent? Consume(SelectedSessionIdentity session);

    /// <summary>Returns a refused attempt to selection without overwriting a newer intent.</summary>
    void RestoreIfAbsent(SelectedSessionIdentity session, DungeonEntryIntent intent);

    bool ClearExact(SelectedSessionIdentity session);
}
