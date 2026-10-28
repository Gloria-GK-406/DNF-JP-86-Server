using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Quests;

public sealed record QuestItemRequirementState(int ItemId, int Required, long Held)
{
    public long Missing => Math.Max(0, Required - Held);
}
public sealed record AcceptedQuestState(CarriedQuest Quest, IReadOnlyList<QuestItemRequirementState> Items)
{
    public bool? CompletionAllowed { get; init; }
    public bool CanComplete => CompletionAllowed ?? new QuestTrigger(unchecked((uint)Quest.TriggerValue)).IsComplete;
}
/// <summary>Quantities are authoritative; shared materials absent from this map are zero.</summary>
public sealed record QuestHoldingsFacts(IReadOnlyDictionary<int, long> Quantities);

/// <summary>Pure effective-state calculation, also used against settlement preview facts.</summary>
public interface IQuestStateRuleService
{
    QuestHoldingsFacts Holdings(IEnumerable<ItemSnapshot> main, IEnumerable<ItemSnapshot> shared, int gold);
    ValueTask<IReadOnlyList<AcceptedQuestState>> EvaluateAsync(IReadOnlyList<CarriedQuest> carried,
        IReadOnlyList<ClearedQuest> cleared, QuestHoldingsFacts holdings, CancellationToken ct);
}
/// <summary>Read-only external facts; never mutates containers or quest progress.</summary>
public interface IQuestFactsDomainService
{
    ValueTask<QuestHoldingsFacts> HoldingsAsync(CharacterId character, CancellationToken ct);
    /// <summary>Durable holdings for queued consumers, independent of online container bindings.</summary>
    ValueTask<QuestHoldingsFacts> PersistentHoldingsAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask<WorldContentSubject> SubjectAsync(AccountId account, CharacterId character, CancellationToken ct);
}
/// <summary>Quest-owned eligibility rules; World is only a projection consumer.</summary>
public interface IQuestAvailabilityRuleService
{
    ValueTask<AvailableQuestEntryState> ReadAvailableAsync(WorldContentSubject subject, CancellationToken ct);
    ValueTask<bool> MeetsRequirementsAsync(QuestDefinition quest, WorldContentSubject subject, CancellationToken ct);
}
/// <summary>Both reads are side-effect free. Accepted/ready is distinct from settled/completed.</summary>
public interface IQuestQueryDomainService
{
    ValueTask<IReadOnlyList<AcceptedQuestState>> ReadAcceptedAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask<AvailableQuestEntryState> ReadAvailableAsync(AccountId account, CharacterId character, CancellationToken ct);
}
