using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// The only holder of process-local dungeon run state: the run directory, its lock, the claim sequence and the
/// end of a run. Each run's own state, claims included, lives on its private <see cref="DungeonRunAggregate"/>.
/// </summary>
/// <remarks>
/// The run services are nested here so that only they reach the aggregates; each is its own class with its own
/// dependencies and registration, and none holds state (<c>DungeonRunServiceStructureTests</c>).
/// </remarks>
internal sealed partial class DungeonRunStore(IPartyService parties, IDungeonDeadlineWake deadlines,
    IDungeonParticipationDomainService participation, IWeaknessDomainService weakness, ICreatureRuntimeDomainService creatures)
    : IDungeonRunQueryDomainService
{
    private readonly object gate = new();
    // A party holds at most one run. Who is in it is the party's answer, so there is no
    // character-to-run index to keep in step with party changes.
    private readonly Dictionary<PartyKey, DungeonRunAggregate> runsByParty = new();
    private readonly Dictionary<DungeonRunId, DungeonRunAggregate> runsById = new();
    private long nextClaim;

    public DungeonRunState? Find(CharacterId characterId)
    {
        lock (this.gate)
        {
            return this.RunOf(characterId)?.ViewFor(characterId);
        }
    }

    public DungeonRunState? Find(DungeonRunId runId)
    {
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var aggregate))
                return null;

            var active = aggregate.Participants
                .OrderBy(participant => participant.Slot)
                .FirstOrDefault();
            return active is null ? null : aggregate.ViewFor(active.CharacterId);
        }
    }

    private long NextClaim() => Interlocked.Increment(ref this.nextClaim);

    /// <summary>The run character lookups reach: the one bound to the character's current party.</summary>
    private DungeonRunAggregate? RunOf(CharacterId characterId) =>
        this.runsByParty.TryGetValue(parties.KeyOf(characterId), out var aggregate)
            && aggregate.IsMember(characterId)
                ? aggregate
                : null;

    /// <summary>The claims of a run still in the directory; a removed run honours none.</summary>
    private RunClaims? ClaimsOf(DungeonRunId runId) =>
        this.runsById.TryGetValue(runId, out var aggregate) ? aggregate.Claims : null;

    private void Publish(DungeonRunAggregate aggregate)
    {
        this.runsById.Add(aggregate.RunId, aggregate);
        this.runsByParty.Add(aggregate.PartyKey, aggregate);
    }

    private void Remove(DungeonRunAggregate aggregate)
    {
        this.runsById.Remove(aggregate.RunId);
        if (this.runsByParty.GetValueOrDefault(aggregate.PartyKey) == aggregate)
            this.runsByParty.Remove(aggregate.PartyKey);
        deadlines.Wake(DungeonDeadlineKind.RoomMechanism);
        aggregate.Claims.ReleaseOnRemoval();
    }

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
        participation.ReleaseCommitted(characterId, new(DungeonParticipationMode.Ordinary, aggregate.RunId.Value));
        weakness.For(characterId).ResumeRecovery();
        creatures.ChangeCombat(characterId, false);
        aggregate.ReadyParticipants.Remove(characterId);
        aggregate.DeadParticipants.Remove(characterId);
        aggregate.EntryRevision++;
        aggregate.Claims.Settlements.Remove(characterId);
        foreach (var key in aggregate.Claims.Cards
                     .Where(entry => entry.Value.CharacterId == characterId
                         && !aggregate.Claims.PreparedCards.Contains(entry.Key))
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            aggregate.Claims.Cards.Remove(key);
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

    /// <summary>Opens the clear of the current room, for settlement and for an altar boss room emptied by a dynamic batch.</summary>
    private DungeonClearChange? TryOpenClear(CharacterId characterId, DungeonRunId expectedRunId, long expectedRoomVersion,
        DateTimeOffset at)
    {
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion
                || !aggregate.IsMember(characterId)
                || aggregate.Layout.Mode is DungeonRunMode.LostLand
                || aggregate.Cells[aggregate.CurrentCell].ClearOpen)
            {
                return null;
            }

            var cell = aggregate.Cells[aggregate.CurrentCell];
            cell.ClearOpen = true;
            aggregate.ClearedAt = at;
            cell.Revision++;
            aggregate.RuntimeRevision++;
            return new DungeonClearChange(
                aggregate.RunId,
                aggregate.RuntimeRevision,
                [.. aggregate.Participants
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)]);
        }
    }
}
