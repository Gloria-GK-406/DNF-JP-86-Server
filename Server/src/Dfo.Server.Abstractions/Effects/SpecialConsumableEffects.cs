using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Effects;

public sealed record FatigueStateFacts(ushort Total, CharacterFatigueRow? Raw);
public sealed record QuestStateFacts(ImmutableArray<StoredActiveQuest> Carried,
    ImmutableArray<StoredTrackedQuest> Tracked, ImmutableArray<StoredClearedQuest> Cleared);
public sealed record SetFatigueStateEffect(CharacterFatigueRow? Expected, CharacterFatigueRow Value) : DomainEffect;
public sealed record SetItemUsageStateEffect(int ItemId, ConsumableUsageRow? Expected, ConsumableUsageRow Value) : DomainEffect;
public sealed record SetCarriedQuestEffect(StoredActiveQuest Expected, StoredActiveQuest? Value) : DomainEffect;
public sealed record RemoveTrackedQuestEffect(StoredTrackedQuest Expected) : DomainEffect;
public sealed record SetClearedQuestEffect(int QuestId, byte? ExpectedMark, byte Mark) : DomainEffect;
public sealed record SetLearnedSkillEffect(int Page, int Slot, int SkillId, byte? ExpectedLevel, byte? Level) : DomainEffect;
public sealed record RenamePetEffect(int ItemUid, int ExpectedTemplateId, ImmutableArray<byte> Name) : DomainEffect;
public sealed record FatigueStateChange(CharacterFatigueRow Value) : EffectChange;
public sealed record ItemUsageStateChange(int ItemId, ConsumableUsageRow Value) : EffectChange;
public sealed record QuestRowChange(int QuestId) : EffectChange;
public sealed record LearnedSkillChange(int Page, int Slot, int SkillId, byte? Level) : EffectChange;
public sealed record PetNameChange(CreatureState Creature) : EffectChange;
public sealed record QuestCouponPlan(ImmutableArray<int> Completed, ImmutableArray<DomainEffect> Effects);
public sealed record RefundSkillPlan(CharacterSkillLearningRejection Rejection,
    ImmutableArray<CharacterSkillState> Changed, CharacterSkillPointLedger? Ledger);
public sealed record FatiguePotionFacts(BusinessSlot Source, ContainerSlotItem Item, byte Level, bool InTown,
    FatigueStateFacts Fatigue, ConsumableUsageRow? Usage, DateTimeOffset At);
public sealed record PetNameFacts(BusinessSlot Source, ContainerSlotItem? Card, ContainerSlotItem? Worn,
    CreatureState? Current, ImmutableArray<byte> Name, DateTimeOffset At);

public interface ISpecialConsumableEffectPlanRuleService
{
    ValueTask<ImmutableArray<DomainEffect>> FatigueAsync(FatiguePotionFacts facts, CancellationToken ct);
    ValueTask<QuestCouponPlan> CouponAsync(BusinessSlot source, ContainerSlotItem item, QuestStateFacts state,
        IReadOnlyList<int> selected, DateTimeOffset at, CancellationToken ct);
    ValueTask<ImmutableArray<DomainEffect>> RefundAsync(BusinessSlot source, ContainerSlotItem water, int page,
        IReadOnlyList<CharacterSkillState> before, IReadOnlyList<CharacterSkillState> changed, CancellationToken ct);
    RefundSkillPlan RefundSkills(CharacterSkillFacts facts, int page, IReadOnlyList<CharacterSkillLearningChange> changes, int learningLevel);
    ImmutableArray<DomainEffect> Rename(PetNameFacts facts);
}

/// <summary>Reads raw state and writes decisions; no game rule or clock is evaluated while applying.</summary>
public interface IConsumableStateDomainService
{
    ValueTask<FatigueStateFacts> FatigueAsync(CharacterId id, CancellationToken ct);
    ValueTask<ConsumableUsageRow?> UsageAsync(CharacterId id, int itemId, CancellationToken ct);
    ValueTask<QuestStateFacts> QuestsAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask SetFatigueAsync(CharacterId id, SetFatigueStateEffect effect, CancellationToken ct);
    ValueTask SetUsageAsync(CharacterId id, SetItemUsageStateEffect effect, CancellationToken ct);
    ValueTask SetCarriedAsync(AccountId account, CharacterId id, SetCarriedQuestEffect effect, CancellationToken ct);
    ValueTask RemoveTrackedAsync(AccountId account, CharacterId id, RemoveTrackedQuestEffect effect, CancellationToken ct);
    ValueTask SetClearedAsync(AccountId account, CharacterId id, SetClearedQuestEffect effect, CancellationToken ct);
    ValueTask SetSkillAsync(CharacterId id, SetLearnedSkillEffect effect, CancellationToken ct);
}
