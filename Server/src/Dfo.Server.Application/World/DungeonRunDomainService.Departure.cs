using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    // Callers retain the admitted-session transition lease through this async exit.
    public async ValueTask<bool> EndAsync(CharacterId characterId, DungeonRunId expectedRunId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(EndAsync));
        RunDeparture? departure;
        lock (this.gate)
            departure = this.Depart(characterId, expectedRunId);
        if (departure is null)
            return false;
        await this.RecordDeparturesAsync(departure).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<IReadOnlyList<CharacterId>> EndRunAsync(DungeonRunId runId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(EndRunAsync));
        RunDeparture departure;
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var aggregate))
                return [];
            departure = new(aggregate.RunId, aggregate.Layout.DungeonId, FailsOnDeparture(aggregate),
                this.EndRun(aggregate));
        }
        await this.RecordDeparturesAsync(departure).ConfigureAwait(false);
        return departure.Characters;
    }

    public bool End(CharacterId characterId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(End));
        lock (this.gate)
        {
            // A character who already left the party no longer reaches the run through it; the run
            // still holding their state is the one they are leaving.
            var aggregate = this.RunOf(characterId)
                ?? this.runsById.Values.FirstOrDefault(run => run.HoldsRunState(characterId));
            return aggregate is not null && this.Depart(characterId, aggregate.RunId) is not null;
        }
    }

    public bool End(CharacterId characterId, DungeonRunId expectedRunId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(End));
        lock (this.gate)
            return this.Depart(characterId, expectedRunId) is not null;
    }

    /// <summary>
    /// One character's departure from a run. Null when the run is gone, the character holds no state
    /// in it, or the character is still a member beside others: membership is the party's, so a
    /// member leaves the party before leaving the run. The last member's departure ends the run.
    /// </summary>
    private RunDeparture? Depart(CharacterId characterId, DungeonRunId runId)
    {
        if (!this.runsById.TryGetValue(runId, out var aggregate) || !aggregate.HoldsRunState(characterId))
            return null;
        var members = aggregate.Participants;
        var isMember = members.Any(member => member.CharacterId == characterId);
        if (isMember && members.Length > 1)
            return null;
        var failed = FailsOnDeparture(aggregate);
        IReadOnlyList<CharacterId> departed = isMember || members.Length == 0
            ? this.EndRun(aggregate)
            : [this.Release(aggregate, characterId)];
        return new(aggregate.RunId, aggregate.Layout.DungeonId, failed, departed);
    }

    private static bool FailsOnDeparture(DungeonRunAggregate aggregate) =>
        aggregate.Layout.AppliesFailureWeakness && !aggregate.Cells[aggregate.CurrentCell].ClearOpen;

    private async ValueTask RecordDeparturesAsync(RunDeparture departure)
    {
        foreach (var characterId in departure.Characters)
        {
            if (this.lifecycle is not null)
                await this.lifecycle.RecordDepartureAsync(
                    new(characterId, departure.RunId, departure.DungeonId)).ConfigureAwait(false);
            if (departure.Failed && this.weakness is not null)
                await this.weakness.For(characterId).SetWeakness().ConfigureAwait(false);
        }
    }

    private sealed record RunDeparture(
        DungeonRunId RunId, int DungeonId, bool Failed, IReadOnlyList<CharacterId> Characters);

    /// <summary>
    /// What leaving does to one character on every path (Spec RQ-008): the occupancy, recovery,
    /// pet-combat and room marks the run holds for them, and their pending settlement and card
    /// reservations. Lifecycle and failure weakness belong to the paths that record them.
    /// </summary>
    /// <remarks>
    /// The character's progress stays: a clear-card commit prepared before they left still
    /// completes against it. It is only marked released, so the departure happens once.
    /// </remarks>
    private CharacterId Release(DungeonRunAggregate aggregate, CharacterId characterId)
    {
        aggregate.ProgressFor(characterId).Released = true;
        this.participation?.ReleaseCommitted(characterId, new(DungeonParticipationMode.Ordinary, aggregate.RunId.Value));
        this.weakness?.For(characterId).ResumeRecovery();
        this.creatures?.ChangeCombat(characterId, false);
        aggregate.ReadyParticipants.Remove(characterId);
        aggregate.DeadParticipants.Remove(characterId);
        aggregate.EntryRevision++;
        foreach (var key in this.settlementClaims.Keys
                     .Where(key => key.RunId == aggregate.RunId && key.CharacterId == characterId)
                     .ToArray())
        {
            this.settlementClaims.Remove(key);
        }
        foreach (var key in this.clearRewardCardClaims
                     .Where(entry => entry.Key.RunId == aggregate.RunId
                         && entry.Value.CharacterId == characterId
                         && !this.preparedClearRewardCardClaims.Contains(entry.Key))
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            this.clearRewardCardClaims.Remove(key);
        }
        return characterId;
    }

    /// <summary>Releases everyone still holding state in the run, then removes it.</summary>
    private IReadOnlyList<CharacterId> EndRun(DungeonRunAggregate aggregate)
    {
        var departing = aggregate.Unreleased();
        foreach (var characterId in departing)
            _ = this.Release(aggregate, characterId);
        this.Remove(aggregate);
        return departing;
    }
}
