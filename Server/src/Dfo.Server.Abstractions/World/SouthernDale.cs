using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public sealed record SouthernDaleDefinition(DungeonDefinition Dungeon, int PartyLimit,
    IReadOnlyList<ItemDeliveryGrant> ClearRewards);
public interface ISouthernDaleContentSource
{ ValueTask<SouthernDaleDefinition> ReadAsync(CancellationToken ct); }
public interface ISouthernDaleRuleService : IOnlineRefilledTicketRule
{
    bool Supports(int dungeonId);
    int EventId { get; }
    EventConfiguration InitialActivity { get; }
    string ActivityLink { get; }
    ValueTask<SouthernDaleDefinition> DefinitionAsync(CancellationToken ct);
}
public interface ISouthernDaleDomainStore
{
    ValueTask<bool> TryClaimClearAsync(Guid runId, CharacterId character, CancellationToken ct);
    ValueTask<bool> HasCompletedDailyQuestAsync(CharacterId character, DateOnly period, CancellationToken ct);
    ValueTask<bool> TryCompleteDailyQuestAsync(CharacterId character, DateOnly period, CancellationToken ct);
}
public interface ISouthernDaleQuestDomainService
{
    const int QuestId = 20549;
    ValueTask<bool> CanAcceptAsync(CharacterId character, CancellationToken ct);
    ValueTask<bool> TryCompleteAsync(CharacterId character, CancellationToken ct);
}
public interface ISouthernDaleDomainService : IDungeonBossClearHook, IOnlineTicketRefillDomainService
{
    bool Supports(int dungeonId);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
}
public sealed record SouthernDaleInventoryChanged(CharacterId CharacterId,
    ImmutableArray<BusinessSlot> Slots) : IDomainEvent;
public sealed record SouthernDaleDailyRefillCompleted(CharacterId CharacterId,
    ImmutableArray<BusinessSlot> Slots) : IDomainEvent;
