using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Effects;

public sealed record ExperienceAwardDecision(CharacterProgressionState Before, ExperienceAward Award);
public sealed record CharacterExperienceEffect(ExperienceAwardDecision Decision) : DomainEffect;
public sealed record CharacterExperienceChange(ExperienceAward Award) : EffectChange;
public sealed record SetQuestRewardGoldEffect(int Expected, int Value) : InventoryEffect;
public sealed record QuestRewardGoldChange(int Before, int After) : EffectChange;
public sealed record SetCharacterGrowTypeEffect(byte Job, byte Expected, byte Value) : DomainEffect;
public sealed record CharacterGrowTypeChange(byte GrowType) : EffectChange;
public sealed record MaterializeSkillBuildsEffect(SkillBuildId Active, ImmutableArray<CharacterSkillBuild> Expected) : DomainEffect;
public sealed record SkillBuildsMaterializedChange : EffectChange;
public sealed record GrantBasicCharacterItemEffect(int ItemId, int Count, ItemPlacement Placement) : InventoryEffect;
public sealed record BasicCharacterItemChange(ImmutableArray<ContainerItemAddition> Additions) : EffectChange;
public sealed record QuestCompletionFacts(CharacterIdentityState Identity, CharacterProgressionState Progress,
    QuestStateFacts Quests, IReadOnlyList<ContainerSlotItem> Main, CarriedGoldRow Gold,
    CharacterSkillFacts? Skills, CharacterSkillBuilds? Builds);
public sealed record QuestCompletionPlan(ushort QuestId, ImmutableArray<DomainEffect> Effects,
    ExperienceAward Award, QuestProgressSnapshot? Projection, byte? GrowType);
public sealed record QuestCompletionReceipt(bool Finished, ushort QuestId, string? Objective,
    ExperienceAward? Award, ApplyEffectsResult? Applied, QuestProgressSnapshot? Projection, byte? GrowType);
public interface IQuestCompletionDomainService
{
    ValueTask<QuestCompletionReceipt> FinishAsync(AccountId account, CharacterId character, ushort quest,
        ushort selection, ushort count, DateTimeOffset at, CancellationToken ct, SelectedSessionIdentity? session = null);
}
public interface IQuestCompletionEffectRuleService
{
    ValueTask<QuestDefinition?> DefinitionAsync(int id, CancellationToken ct);
    QuestRewardResolution Rewards(QuestDefinition definition, QuestCompletionFacts facts, ushort selection, ushort count);
    ValueTask<ImmutableArray<CarriedQuest>> EffectiveAsync(QuestStateFacts quests, IReadOnlyList<ContainerSlotItem> main, int gold, CancellationToken ct);
    ValueTask<ImmutableArray<DomainEffect>> RewardsAsync(QuestCompletionRewardPlan reward, QuestCompletionFacts facts, CancellationToken ct);
    ValueTask<QuestCompletionPlan> CompleteAsync(ushort questId, QuestCompletionRewardPlan reward,
        QuestCompletionFacts facts, ImmutableArray<DomainEffect> rewards, InventoryEffectHoldingsSnapshot after, CancellationToken ct);
}
