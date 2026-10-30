using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class DungeonExitDomainService(IPartyService parties, IDungeonRunDepartureDomainService runs) : IDungeonExitDomainService
{
    public bool LeavesRun(DungeonRunState? run, bool chosenLeave) =>
        run?.Layout.Mode is DungeonRunMode.Altar or DungeonRunMode.Guardian || chosenLeave;

    // Native retry follows the exit with 000F(dungeonId). Retain the ordinary
    // maze partition rather than treating that nonzero target as a story request.
    public DungeonEntryIntent? RechallengeIntent(DungeonRunState run, bool retryChosen) =>
        retryChosen && run.Layout is { Purpose.Kind: DungeonRunPurposeKind.Ordinary, Mode.Capabilities.AllowsOrdinaryRechallenge: true }
            ? DungeonEntryIntent.OrdinaryRechallenge(run.Layout.DungeonId)
            : null;

    public async ValueTask<IReadOnlyList<CharacterId>> EndWholeRunAsync(DungeonRunId runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await runs.EndRunAsync(runId).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<CharacterId>> ReturnToSelectionAsync(SelectedSessionIdentity session, DungeonRunState? run, CancellationToken cancellationToken)
    {
        // Only the run's leader takes the whole party back: the run ends for all its members at
        // once, and a member's request leaves it running. The run is reached through the
        // requester's party, so that party's leader is the run's. A selection-intent holder
        // normally has no run at all, so this is defensive.
        return run is not null
            && parties.FindParty(new PartyCharacterSnapshot(
                session.AccountId, session.CharacterId, session.WireUserId, null)).LeaderWireUserId == session.WireUserId
            ? await runs.EndRunAsync(run.RunId).ConfigureAwait(false)
            : [];
    }

    public async ValueTask<DungeonGiveUpResult> GiveUpAsync(CharacterId characterId, DungeonRunId runId, CancellationToken cancellationToken)
    {
        // The run's members are its party's, so giving up leaves the party first; the party
        // mutation then ends this character's part of the run. Outside a real party the leave
        // is refused and the run, which is theirs alone, ends below.
        var party = await LeavePartyAsync(characterId, cancellationToken).ConfigureAwait(false);
        var ended = party.Succeeded && runs.Find(characterId)?.RunId != runId;
        if (!ended)
            ended = await runs.EndAsync(characterId, runId).ConfigureAwait(false);
        return new(party, ended);
    }

    public ValueTask<PartyOperationResult> LeavePartyAsync(CharacterId characterId, CancellationToken cancellationToken) =>
        DepartAsync(characterId, () => parties.LeaveParty(characterId), cancellationToken);

    public ValueTask<PartyOperationResult> RemoveFromPartyAsync(CharacterId caller, CharacterId target, CancellationToken cancellationToken) =>
        DepartAsync(target, () => parties.RemoveMember(caller, target), cancellationToken);

    private async ValueTask<PartyOperationResult> DepartAsync(CharacterId departing, Func<PartyOperationResult> change, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var run = runs.Find(departing);
        var result = change();
        if (result.Succeeded && run is not null)
            _ = await runs.EndAsync(departing, run.RunId).ConfigureAwait(false);
        return result;
    }
}
