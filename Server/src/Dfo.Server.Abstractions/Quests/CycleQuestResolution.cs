using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Quests;

public sealed record CycleQuestAcceptance(AccountId AccountId, CharacterId CharacterId, long ConnectionGeneration, short Slot, DateTimeOffset AcceptedAt, string ConnectionKey);
public sealed record CycleQuestReward(BusinessSlot Slot, int ItemId, int Count);
/// <summary>A committed disposition of the acceptance frozen in one dungeon clear.</summary>
public sealed record CycleQuestResolved(DungeonRunId RunId, CycleQuestAcceptance Acceptance,
    ushort QuestId, bool Finished, string? Objective, uint Experience, DateTimeOffset At,
    ImmutableArray<CycleQuestReward> Rewards) : IDomainEvent;
public sealed record CycleQuestResolutionView(CycleQuestResolved Result) : INotification<CycleQuestResolutionView>;
public interface ICycleQuestResolutionDomainService
{
    ValueTask ResolveAsync(DungeonMapCleared clear, CancellationToken ct);
}
