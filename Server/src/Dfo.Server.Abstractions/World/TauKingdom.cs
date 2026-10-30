using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public sealed record TauKingdomDefinition(DungeonDefinition Dungeon, int PartyLimit, bool ScaleMonsters);
public interface ITauKingdomContentSource
{ ValueTask<TauKingdomDefinition> ReadAsync(CancellationToken ct); }
public interface ITauKingdomRuleService : IOnlineRefilledTicketRule
{
    bool Supports(int dungeonId);
    int EventId { get; }
    EventConfiguration InitialActivity { get; }
    string ActivityLink { get; }
    ValueTask<TauKingdomDefinition> DefinitionAsync(CancellationToken ct);
}
public interface ITauKingdomDomainService : IOnlineTicketRefillDomainService
{
    bool Supports(int dungeonId);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
}
public sealed record TauKingdomTicketRefilled(CharacterId CharacterId, ImmutableArray<BusinessSlot> Slots) : IDomainEvent;
