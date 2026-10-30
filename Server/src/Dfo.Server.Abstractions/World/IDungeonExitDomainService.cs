using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>What giving up came to.</summary>
/// <param name="Party">The party's answer to the character leaving it.</param>
/// <param name="Ended">Whether the character's part of the run ended.</param>
public sealed record DungeonGiveUpResult(PartyOperationResult Party, bool Ended);

/// <summary>How characters walk out of a run, alone or together.</summary>
/// <remarks>
/// A run's members are its party's, so a member leaves the party before leaving the run; the order
/// is kept here and nowhere else.
/// </remarks>
public interface IDungeonExitDomainService
{
    /// <summary>Whether a leave request ends this run's stay.</summary>
    /// <param name="run">The run the requester is in, if any.</param>
    /// <param name="chosenLeave">Whether the request chose one of the leaving results.</param>
    bool LeavesRun(DungeonRunState? run, bool chosenLeave);

    /// <summary>The intent that lets a native retry re-enter the ordinary maze, when this run allows one.</summary>
    DungeonEntryIntent? RechallengeIntent(DungeonRunState run, bool retryChosen);

    /// <summary>The whole party leaves together: the run ends for every member at once.</summary>
    ValueTask<IReadOnlyList<CharacterId>> EndWholeRunAsync(DungeonRunId runId, CancellationToken cancellationToken);

    /// <summary>Going back to selection: only the run's leader takes the whole party back.</summary>
    ValueTask<IReadOnlyList<CharacterId>> ReturnToSelectionAsync(SelectedSessionIdentity session, DungeonRunState? run, CancellationToken cancellationToken);

    /// <summary>Gives up the run: leaves the party, and ends the run alone when no party took the character out of it.</summary>
    ValueTask<DungeonGiveUpResult> GiveUpAsync(CharacterId characterId, DungeonRunId runId, CancellationToken cancellationToken);

    /// <summary>Leaves the party, then ends the character's part of any run.</summary>
    ValueTask<PartyOperationResult> LeavePartyAsync(CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>Removes a member from the party, then ends that member's part of any run.</summary>
    ValueTask<PartyOperationResult> RemoveFromPartyAsync(CharacterId caller, CharacterId target, CancellationToken cancellationToken);
}
