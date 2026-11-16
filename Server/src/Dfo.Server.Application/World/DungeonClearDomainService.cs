using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

/// <summary>
/// Turns whichever authoritative runtime fact just changed into the one shared clear transition.
/// </summary>
public sealed class DungeonClearDomainService(
    IDungeonRunSettlementDomainService runs,
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
            || run.ClearOpen)
        {
            return null;
        }

        // 70 IsClearDungeon @ 0x0822D08C: a maze with clear conditions clears on them alone, in
        // whatever room the last one is met, and only once; otherwise the boss room does, unless
        // the dungeon ignores that default.
        if (run.Layout.ClearsOnConditions)
        {
            if (run.ClearedAt is not null || !DungeonClearConditionRules.AreMet(run))
                return null;
        }
        else if (run.Layout.IgnoresDefaultClear
            || !run.IsBossRoom
            || !await rooms.IsCurrentRoomClearedAsync(run, cancellationToken).ConfigureAwait(false))
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
