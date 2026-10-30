using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    public DungeonEntryChange PreparePartyEntry(
        IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout,
        DungeonDifficulty difficulty,
        DungeonCellSeed initialCell,
        DungeonRunId runId,
        DateTimeOffset startedAt)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(PreparePartyEntry));
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(initialCell);
        if (!initialCell.IsComplete)
            throw new ArgumentException("A dungeon run cannot publish an incomplete Cell.", nameof(initialCell));
        if (initialCell.Cell != layout.StartCell
            || initialCell.MapId != layout.StartRoomMapId)
        {
            throw new ArgumentException(
                "The initial Cell identity must match the layout's start room.", nameof(initialCell));
        }
        if (participants.Count == 0)
            throw new ArgumentException("A dungeon run requires at least one participant.", nameof(participants));
        if (participants.Any(participant => participant is null))
            throw new ArgumentException("A dungeon participant cannot be null.", nameof(participants));
        if (participants.Select(participant => participant.CharacterId).Distinct().Count()
            != participants.Count)
        {
            throw new ArgumentException(
                "A character can occur only once in a dungeon run.", nameof(participants));
        }
        if (participants.Select(participant => participant.Slot).Distinct().Count()
            != participants.Count)
        {
            throw new ArgumentException(
                "A dungeon participant slot can occur only once.", nameof(participants));
        }
        if (participants.Count(participant => participant.IsLeader) != 1)
        {
            throw new ArgumentException(
                "A dungeon run requires exactly one leader.", nameof(participants));
        }

        var frozen = participants.ToImmutableArray();
        var entryLeader = frozen.Single(participant => participant.IsLeader);
        var partyKey = this.parties.FindParty(new PartyCharacterSnapshot(
            entryLeader.AccountId, entryLeader.CharacterId, 0, null)).Key;
        var currentParty = this.parties.FindByKey(partyKey)
            ?? throw new InvalidOperationException("The entry party no longer exists.");
        if (!currentParty.Members.Select(member => member.CharacterId)
                .ToHashSet().SetEquals(frozen.Select(participant => participant.CharacterId)))
            throw new InvalidOperationException("The entry roster no longer matches the party.");
        if (this.participation is not null && !this.participation.TryClaim(
            frozen.Select(x => x.CharacterId).ToArray(), new(DungeonParticipationMode.Ordinary, runId.Value)))
            throw new InvalidOperationException("A dungeon participant is already occupied.");
        var aggregate = new DungeonRunAggregate(
            runId, partyKey, this.parties, frozen, layout, difficulty, startedAt, initialCell);

        lock (this.gate)
        {
            if (this.transactions?.Current is { } transaction)
            {
                var previous = this.runsByParty.GetValueOrDefault(partyKey);
                transaction.Enlist(new object(), () => new EntryCommit(this, aggregate, previous));
                return aggregate.EntryChange(DungeonEntryChangeKind.Prepared, aggregate.Leader.CharacterId);
            }
            // The party's earlier run ends here without a departure: nobody left, they started over.
            if (this.runsByParty.GetValueOrDefault(partyKey) is { } replaced)
                _ = this.EndRun(replaced);

            this.Publish(aggregate);
            foreach (var participant in frozen)
                this.weakness?.For(participant.CharacterId).PauseRecovery();

            return aggregate.EntryChange(
                DungeonEntryChangeKind.Prepared,
                aggregate.Leader.CharacterId);
        }
    }

    public DungeonEntryChange? TryMarkParticipantReady(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryMarkParticipantReady));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion
                || !aggregate.ReadyParticipants.Add(characterId))
            {
                return null;
            }

            if (aggregate.Layout.Mode is DungeonRunMode.LostLand { Plan: var exploration } && aggregate.LostLandExploration is null)
                aggregate.LostLandExploration = new(this.clock.GetUtcNow().AddSeconds(exploration.FirstLimitSeconds));
            aggregate.EntryRevision++;
            if (aggregate.Participants.All(member => aggregate.ReadyParticipants.Contains(member.CharacterId)))
            {
                var cell = aggregate.Cells[aggregate.CurrentCell];
                cell.Mechanisms = cell.Mechanisms.Select(state => this.mechanismRules.Start(state, this.clock.GetUtcNow())).ToArray();
                if (cell.IsCleared)
                    cell.Mechanisms = cell.Mechanisms.Select(state => this.mechanismRules.Complete(state, this.clock.GetUtcNow())).ToArray();
                this.deadlines?.Wake(DungeonDeadlineKind.RoomMechanism);
            }
            return aggregate.EntryChange(DungeonEntryChangeKind.ParticipantReady, characterId);
        }
    }

    public DungeonEntryChange? FindEntryChange(CharacterId characterId)
    {
        lock (this.gate)
        {
            return this.RunOf(characterId) is { } aggregate
                ? aggregate.EntryChange(DungeonEntryChangeKind.Snapshot, characterId)
                : null;
        }
    }

    public bool TryAbortEntry(
        DungeonRunId expectedRunId,
        long expectedEntryRevision,
        IReadOnlyList<CharacterId> expectedRecipients)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryAbortEntry));
        ArgumentNullException.ThrowIfNull(expectedRecipients);
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(expectedRunId, out var aggregate)
                || aggregate.EntryRevision != expectedEntryRevision
                || !aggregate.Participants.Select(participant => participant.CharacterId)
                    .ToHashSet().SetEquals(expectedRecipients))
            {
                return false;
            }

            _ = this.EndRun(aggregate);
            return true;
        }
    }
}
