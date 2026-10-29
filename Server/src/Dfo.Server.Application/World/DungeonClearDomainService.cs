using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

/// <summary>
/// Turns whichever authoritative runtime fact just changed into the one shared clear transition.
/// </summary>
public sealed class DungeonClearDomainService(
    IDungeonRunDomainService runs,
    IMonsterKillDomainService rooms) : IDungeonClearDomainService
{
    public async ValueTask<DungeonClearChange?> TryOpenClearAsync(
        AccountId accountId,
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        CancellationToken cancellationToken)
    {
        var run = runs.Find(characterId);
        if (run is null || run.RunId != expectedRunId)
            run = runs.Find(expectedRunId);
        if (run is null
            || run.RunId != expectedRunId
            || run.RoomVersion != expectedRoomVersion
            || !run.Layout.Mode.Capabilities.EndsOnBossClear
            || run.ClearOpen
            || !run.IsBossRoom
            || !await rooms.IsCurrentRoomClearedAsync(run, cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        // The room's verified clear is the dungeon fact. Quest objectives consume that fact;
        // they cannot be a prerequisite for opening the very clear that completes them.
        var activeParticipant = run.Participant
            ?? throw new InvalidOperationException("An active run view requires one participant.");
        return runs.TryOpenClear(
            activeParticipant.CharacterId, expectedRunId, expectedRoomVersion);
    }
}
