using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

/// <summary>
/// Turns whichever authoritative runtime fact just changed into the one shared clear transition.
/// </summary>
public sealed class DungeonClearDomainService(
    IDungeonRunDomainService runs,
    IMonsterKillDomainService rooms,
    IDungeonSettlementRuleService settlements) : IDungeonClearDomainService
{
    public async ValueTask<DungeonClearChange?> EvaluateAsync(
        AccountId accountId,
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        CancellationToken cancellationToken)
    {
        var run = runs.Find(characterId);
        if (run is null
            || run.RunId != expectedRunId
            || run.RoomVersion != expectedRoomVersion
            || run.ClearOpen
            || !run.IsBossRoom
            || !await rooms.IsCurrentRoomClearedAsync(run, cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        // A party run's story purpose is frozen from the leader's entry intent. Helpers may
        // trigger re-evaluation, but their unrelated quest logs cannot satisfy or block it.
        var leader = run.PartyMemberCount > 1
            ? run.Participants.Single(participant => participant.IsLeader)
            : null;
        var rules = await settlements.ForAsync(
            leader?.AccountId ?? accountId,
            leader?.CharacterId ?? characterId,
            run,
            cancellationToken).ConfigureAwait(false);
        return rules.CanComplete
            ? runs.TryOpenClear(characterId, expectedRunId, expectedRoomVersion)
            : null;
    }
}
