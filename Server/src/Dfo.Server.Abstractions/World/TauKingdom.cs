using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public sealed record TauKingdomDefinition(DungeonDefinition Dungeon, int PartyLimit, bool ScaleMonsters);
public interface ITauKingdomContentSource
{ ValueTask<TauKingdomDefinition> ReadAsync(CancellationToken ct); }
public interface ITauKingdomRuleService
{
    bool Supports(int dungeonId);
    int EventId { get; }
    int TicketItemId { get; }
    EventConfiguration InitialActivity { get; }
    string ActivityLink { get; }
    ValueTask<TauKingdomDefinition> DefinitionAsync(CancellationToken ct);
}
public interface ITauKingdomDomainService
{
    bool Supports(int dungeonId);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
    ValueTask<bool> RefillOnlineTicketAsync(SelectedSessionIdentity session, CancellationToken ct);
}
public sealed record TauKingdomTicketRefilled(CharacterId CharacterId, ImmutableArray<BusinessSlot> Slots) : IDomainEvent;
