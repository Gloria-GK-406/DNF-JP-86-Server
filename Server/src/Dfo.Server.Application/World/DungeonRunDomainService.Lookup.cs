using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
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

    /// <summary>The run character lookups reach: the one bound to the character's current party.</summary>
    private DungeonRunAggregate? RunOf(CharacterId characterId) =>
        this.runsByParty.TryGetValue(this.parties.KeyOf(characterId), out var aggregate)
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
        this.deadlines.Wake(DungeonDeadlineKind.RoomMechanism);
        aggregate.Claims.ReleaseOnRemoval();
    }
}
