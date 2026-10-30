using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunStore
{
    internal sealed partial class DepartureService
    {
        // Callers retain the admitted-session transition lease through this async exit.
        public async ValueTask<bool> EndAsync(CharacterId characterId, DungeonRunId expectedRunId)
        {
            GameplayWriteAssertion.Writing(nameof(DepartureService) + "." + nameof(EndAsync));
            RunDeparture? departure;
            lock (this.store.gate)
                departure = this.Depart(characterId, expectedRunId);
            if (departure is null)
                return false;
            await this.RecordDeparturesAsync(departure).ConfigureAwait(false);
            return true;
        }

        public async ValueTask<IReadOnlyList<CharacterId>> EndRunAsync(DungeonRunId runId)
        {
            GameplayWriteAssertion.Writing(nameof(DepartureService) + "." + nameof(EndRunAsync));
            RunDeparture departure;
            lock (this.store.gate)
            {
                if (!this.store.runsById.TryGetValue(runId, out var aggregate))
                    return [];
                departure = new(aggregate.RunId, aggregate.Layout.DungeonId, FailsOnDeparture(aggregate),
                    this.store.EndRun(aggregate));
            }
            await this.RecordDeparturesAsync(departure).ConfigureAwait(false);
            return departure.Characters;
        }

        public bool End(CharacterId characterId)
        {
            GameplayWriteAssertion.Writing(nameof(DepartureService) + "." + nameof(End));
            lock (this.store.gate)
            {
                // A character who already left the party no longer reaches the run through it; the run
                // still holding their state is the one they are leaving.
                var aggregate = this.store.RunOf(characterId)
                    ?? this.store.runsById.Values.FirstOrDefault(run => run.HoldsRunState(characterId));
                return aggregate is not null && this.Depart(characterId, aggregate.RunId) is not null;
            }
        }

        public bool End(CharacterId characterId, DungeonRunId expectedRunId)
        {
            GameplayWriteAssertion.Writing(nameof(DepartureService) + "." + nameof(End));
            lock (this.store.gate)
                return this.Depart(characterId, expectedRunId) is not null;
        }

        /// <summary>
        /// One character's departure from a run. Null when the run is gone, the character holds no state
        /// in it, or the character is still a member beside others: membership is the party's, so a
        /// member leaves the party before leaving the run. The last member's departure ends the run.
        /// </summary>
        private RunDeparture? Depart(CharacterId characterId, DungeonRunId runId)
        {
            if (!this.store.runsById.TryGetValue(runId, out var aggregate) || !aggregate.HoldsRunState(characterId))
                return null;
            var members = aggregate.Participants;
            var isMember = members.Any(member => member.CharacterId == characterId);
            if (isMember && members.Length > 1)
                return null;
            var failed = FailsOnDeparture(aggregate);
            IReadOnlyList<CharacterId> departed = isMember || members.Length == 0
                ? this.store.EndRun(aggregate)
                : [this.store.Release(aggregate, characterId)];
            return new(aggregate.RunId, aggregate.Layout.DungeonId, failed, departed);
        }

        private static bool FailsOnDeparture(DungeonRunAggregate aggregate) =>
            aggregate.Layout.AppliesFailureWeakness && !aggregate.Cells[aggregate.CurrentCell].ClearOpen;

        private async ValueTask RecordDeparturesAsync(RunDeparture departure)
        {
            foreach (var characterId in departure.Characters)
            {
                await this.lifecycle.RecordDepartureAsync(
                        new(characterId, departure.RunId, departure.DungeonId)).ConfigureAwait(false);
                if (departure.Failed)
                    await this.weakness.For(characterId).SetWeakness().ConfigureAwait(false);
            }
        }

        private sealed record RunDeparture(
            DungeonRunId RunId, int DungeonId, bool Failed, IReadOnlyList<CharacterId> Characters);
    }
}
