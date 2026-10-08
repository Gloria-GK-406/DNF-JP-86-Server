using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.Achievements;

public sealed record AchievementCondition(string Type, int SubType, IReadOnlyList<int> Values, int Count);
public sealed record AchievementDefinition(int Id, int MinimumLevel, int MaximumLevel,
    AchievementCondition Condition, IReadOnlyList<int> Prerequisites)
{
    public IReadOnlyList<IReadOnlyList<int>> PrerequisiteGroups { get; init; } = [];
    public bool SupportedRestrictions { get; init; } = true;
    public bool? RequiresHardcore { get; init; }
    public IReadOnlyList<string> EquipmentSlots { get; init; } = [];
}
public sealed record TitleBookSlotDefinition(int Category, int Index, int SlotType, int QuestId,
    IReadOnlyList<int> ItemIds)
{
    public bool AllowsItem(int itemId) => itemId > 0 && (ItemIds.Contains(itemId) || ItemIds.Contains(-1)
        || (QuestId == -1 && ItemIds.Count == 0));
}
public sealed record TitleBookCategoryDefinition(int Category, int Capacity, IReadOnlyList<TitleBookSlotDefinition> Slots);
public interface IAchievementDefinitionSource { IReadOnlyList<AchievementDefinition> Load(); }
public interface ITitleBookDefinitionSource { IReadOnlyList<TitleBookCategoryDefinition> Load(); }
public interface IAchievementRuleService
{
    IReadOnlyList<AchievementDefinition> Catalog();
    bool IsSupported(AchievementDefinition definition);
    int InitialRemaining(AchievementDefinition definition);
    int Contribution(AchievementDefinition definition, CharacterGameplayFact fact);
    bool CanCheckSnapshot(AchievementDefinition definition);
    ValueTask<bool?> IsSatisfiedAsync(AchievementDefinition definition, AchievementCharacterFacts facts, CancellationToken ct);
}
public interface ITitleBookRuleService
{
    IReadOnlyList<TitleBookCategoryDefinition> Catalog();
    IReadOnlySet<int> ActivatedAchievements(IReadOnlySet<(int Category, int Index)> unlockedSlots);
    ValueTask<OpeningRewardItemDefinition?> RewardTemplateAsync(int itemId, CancellationToken ct);
}
public sealed record AchievementProgress(int AchievementId, int Remaining, int P2, int P3, int P4, bool Completed);
public sealed record StoredTitleBookItem(int Category, int Index, ItemSnapshot Item);
public interface IAchievementPersistence
{
    ValueTask<IReadOnlyList<AchievementProgress>> ReadAsync(CharacterId character, CancellationToken ct);
    ValueTask EnsureAsync(CharacterId character, int achievementId, int remaining, CancellationToken ct);
    ValueTask<bool> TryConsumeFactAsync(CharacterId character, Guid eventId, CancellationToken ct);
    ValueTask<bool> AdvanceAsync(CharacterId character, int achievementId, int count, CancellationToken ct);
    ValueTask<bool> IsCompletedAsync(CharacterId character, int achievementId, CancellationToken ct);
}
public sealed record AchievementEquippedItem(BusinessSlot Slot, int ItemId);
public sealed record AchievementCharacterFacts(int Level, IReadOnlySet<int> ClearedQuests)
{
    public CharacterCareer? Career { get; init; }
    public bool? Hardcore { get; init; }
    public IReadOnlyDictionary<int, long> ItemCounts { get; init; } = new Dictionary<int, long>();
    public int? AvatarCount { get; init; }
    public IReadOnlyList<int> CreatureItems { get; init; } = [];
    public IReadOnlyList<AchievementEquippedItem> EquippedItems { get; init; } = [];
}
public interface IAchievementCharacterFactsDomainService
{
    ValueTask<AchievementCharacterFacts> ReadAsync(CharacterId character, CancellationToken ct);
    ValueTask<AchievementCharacterFacts> ReadForUnlockAsync(CharacterId character, IReadOnlyList<AchievementDefinition> pending, CancellationToken ct);
}
public interface ITitleBookPersistence
{
    ValueTask<bool> TryUnlockTitleAsync(CharacterId character, int category, int index, int itemId, CancellationToken ct);
    ValueTask<IReadOnlyList<StoredTitleBookItem>> ReadTitlesAsync(CharacterId character, CancellationToken ct);
    ValueTask<IReadOnlyList<(int Category, int Index)>> ReadUnlockedSlotsAsync(CharacterId character, CancellationToken ct);
}
public interface IAchievementDomainService
{
    ValueTask PrepareAsync(CharacterId character, int level, CancellationToken ct);
    ValueTask<IReadOnlyList<CharacterAchievementProgressState>> GetAsync(CharacterId character, CancellationToken ct);
    ValueTask<CharacterAchievementProgressState?> ReconcileAsync(CharacterId character, int achievementId, CancellationToken ct);
    ValueTask<IReadOnlyList<int>> UnlockSatisfiedAchievementsAsync(CharacterId character, CancellationToken ct);
}
public sealed record TitleBookMutationResult(bool Success, string? Reason, int Category, int Index, BusinessSlot Slot)
{
    public bool CarriedInventoryChanged { get; init; }
}
public interface ITitleBookDomainService
{
    ValueTask<TitleBookMutationResult> ToggleEquippedAsync(CharacterId character, int itemId, int category, int index, CancellationToken ct);
    ValueTask<bool> SetAppearanceAsync(CharacterId character, int appearanceItemId, CancellationToken ct);
    ValueTask PrepareAsync(CharacterId character, CancellationToken ct);
    ValueTask<IReadOnlyList<CharacterTitleBookCategoryState>> GetAsync(CharacterId character, CancellationToken ct);
    ValueTask<IReadOnlyList<InventoryItemState>> GetItemsAsync(CharacterId character, int category, CancellationToken ct);
    ValueTask<TitleBookMutationResult> StoreAsync(CharacterId character, BusinessSlot source,
        int itemId, int category, int index, CancellationToken ct);
    ValueTask<TitleBookMutationResult> WithdrawAsync(CharacterId character, BusinessSlot destination,
        int itemId, int category, int index, CancellationToken ct);
}
/// <summary>A successful authoritative gameplay operation, never a client achievement delta.</summary>
public sealed record CharacterGameplayFact(CharacterId CharacterId, string Kind, int SubjectId, int Count = 1,
    int DungeonId = -1, int Difficulty = -1);
public sealed record MonsterDefeated(CharacterId CharacterId, int MonsterId, int Rank, int DungeonId, int Difficulty) : IDomainEvent;
public sealed record DungeonCompleted(CharacterId CharacterId, int DungeonId, int Difficulty) : IDomainEvent;
public sealed record QuestCompleted(CharacterId CharacterId, int QuestId) : IDomainEvent;
public sealed record ItemUsed(CharacterId CharacterId, int ItemId, int Count) : IDomainEvent;
public sealed record MaterialConsumed(CharacterId CharacterId, int ItemId, int Count) : IDomainEvent;
public sealed record CharacterLevelChanged(CharacterId CharacterId, int Before, int After) : IDomainEvent;
public interface ICharacterGameplayFactDomainService
{
    ValueTask RecordMonsterDefeatedAsync(MonsterDefeated fact, CancellationToken ct);
    ValueTask RecordDungeonCompletedAsync(DungeonCompleted fact, CancellationToken ct);
}
public sealed record AchievementProgressChanged(CharacterId CharacterId, int AchievementId) : IDomainEvent;
public sealed record AchievementCompleted(CharacterId CharacterId, int AchievementId) : IDomainEvent;
public sealed record TitleUnlocked(CharacterId CharacterId, int AchievementId, int Category, int Index, int ItemId) : IDomainEvent;
public sealed record TitleAppearanceChanged(CharacterId CharacterId) : IDomainEvent;
public sealed record TitleStored(CharacterId CharacterId, int Category, int Index, BusinessSlot Slot) : IDomainEvent;
public sealed record TitleWithdrawn(CharacterId CharacterId, int Category, int Index, BusinessSlot Slot) : IDomainEvent;
