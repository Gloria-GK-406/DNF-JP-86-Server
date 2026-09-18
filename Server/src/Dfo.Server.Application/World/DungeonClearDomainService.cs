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
        var evaluatorAccountId = accountId;
        var evaluatorCharacterId = characterId;
        if (run is null || run.RunId != expectedRunId)
        {
            run = runs.Find(expectedRunId);
            if (run?.Participant is { } active)
            {
                evaluatorAccountId = active.AccountId;
                evaluatorCharacterId = active.CharacterId;
            }
        }
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
        var purposeOwner = run.EntryPurposeOwner
            ?? (run.PartyMemberCount > 1
                ? run.Participants.Single(participant => participant.IsLeader)
                : null);
        var activeParticipant = run.Participant
            ?? throw new InvalidOperationException("An active run view requires one participant.");
        var rules = await settlements.ForAsync(
            purposeOwner?.AccountId ?? evaluatorAccountId,
            purposeOwner?.CharacterId ?? evaluatorCharacterId,
            run,
            cancellationToken).ConfigureAwait(false);
        return rules.CanComplete
            ? runs.TryOpenClear(
                activeParticipant.CharacterId, expectedRunId, expectedRoomVersion)
            : null;
    }
}
