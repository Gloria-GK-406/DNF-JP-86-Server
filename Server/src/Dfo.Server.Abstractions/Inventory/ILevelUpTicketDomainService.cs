using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record LevelUpTicketResult(OpeningInventoryReceipt Inventory, CharacterProgressionState Progress, CharacterSkillProtocolMirrorState Points,
    ImmutableArray<int> CompletedQuestIds);
public sealed record LevelUpTicketPlan(byte TargetLevel, ImmutableArray<int> CompletedQuestIds,
    ImmutableArray<DomainEffect> Effects);

public interface ILevelUpTicketDomainService
{
    ValueTask<LevelUpTicketResult> UseAsync(SelectedSessionIdentity session, BusinessSlot source,
        DateTimeOffset at, CancellationToken ct);
}

public interface ILevelUpTicketRuleService
{
    ValueTask<LevelUpTicketPlan> PlanAsync(BusinessSlot source, ItemReference item,
        CharacterIdentityState identity, CharacterProgressionState progress, bool hardcore,
        QuestStateFacts quests, DateTimeOffset at, CancellationToken ct);
}
