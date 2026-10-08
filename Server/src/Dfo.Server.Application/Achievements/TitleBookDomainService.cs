using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Achievements;

internal sealed class TitleBookDomainService(ITitleBookRuleService rules, IAchievementPersistence persistence,
    ITitleBookPersistence titlePersistence, IAchievementCharacterFactsDomainService characterFacts,
    IItemDomainStore items, IDomainTransactionAccessor transactions, IItemPlacementRuleService placement,
    IInventorySlotMappingRuleService mapping, IInventoryCapacityDomainService capacity, IEventService events,
    IEquipmentWearDomainService wear) : ITitleBookDomainService, IDomainEventSubscriber<AchievementCompleted>
{
    private static ContainerId Book(CharacterId character, int category) =>
        ContainerId.ForCharacter(character, (InventoryContainerType)((int)InventoryContainerType.TitleBookGeneral + category));
    private IItemDomainStoreSession Session => items.SessionIn(transactions.Current
        ?? throw new InvalidOperationException("Title mutations require a transaction."));
    public async ValueTask<IReadOnlyList<CharacterTitleBookCategoryState>> GetAsync(CharacterId character, CancellationToken ct)
    {
        var rows = await titlePersistence.ReadTitlesAsync(character, ct);
        return rules.Catalog().Select(category => new CharacterTitleBookCategoryState((byte)category.Category, 0, 0,
            rows.Where(x => x.Category == category.Category).Select(x => new CharacterTitleBookEntryState(
                checked((ushort)x.Index), x.Item.ItemId, x.Item.InstanceValue, x.Item.Attribute, x.Item.Durability,
                x.Item.SealFlag, x.Item.EnchantCardId, x.Item.EnchantUpgradeCount, x.Item.AmplifyType, x.Item.AmplifyValue)).ToArray())).ToArray();
    }
    public async ValueTask HandleAsync(DomainEventEnvelope<AchievementCompleted> e, CancellationToken ct)
    {
        var f = e.Payload;
        if (!await persistence.IsCompletedAsync(f.CharacterId, f.AchievementId, ct)) return;
        await UnlockAsync(f.CharacterId, f.AchievementId, ct);
    }
    public async ValueTask PrepareAsync(CharacterId character, CancellationToken ct)
    {
        var unlocked = await GetUnlockedAchievementIdsAsync(character, ct);
        foreach (var row in (await persistence.ReadAsync(character, ct)).Where(x => x.Completed && !unlocked.Contains(x.AchievementId)))
            await UnlockAsync(character, row.AchievementId, ct);
    }
    private async ValueTask<IReadOnlySet<int>> GetUnlockedAchievementIdsAsync(CharacterId character, CancellationToken ct)
    {
        var unlocked = (await titlePersistence.ReadUnlockedSlotsAsync(character, ct)).ToHashSet();
        return rules.ActivatedAchievements(unlocked);
    }
    private async ValueTask UnlockAsync(CharacterId character, int achievementId, CancellationToken ct)
    {
        var unlocked = (await titlePersistence.ReadUnlockedSlotsAsync(character, ct)).ToHashSet();
        foreach (var slot in rules.Catalog().SelectMany(x => x.Slots).Where(x => x.SlotType >= 0 && x.QuestId == achievementId && x.ItemIds.Any(id => id > 0)))
        {
            if (unlocked.Contains((slot.Category, slot.Index))) continue;
            var session = Session;
            var held = await session.GetAsync(Book(character, slot.Category), slot.Index, ct);
            var itemId = slot.ItemIds.First(id => id > 0);
            if (held is not null && !slot.AllowsItem(held.ItemId))
                continue; // Preserve an incompatible legacy instance; never overwrite it with a reward.
            if (!await titlePersistence.TryUnlockTitleAsync(character, slot.Category, slot.Index, itemId, ct)) continue;
            var alreadyOwned = held is not null;
            if (!alreadyOwned)
                foreach (var container in new[] { InventoryContainerType.Main, InventoryContainerType.Equipment })
                    if ((await session.GetAllAsync(ContainerId.ForCharacter(character, container), ct)).Any(x => slot.ItemIds.Contains(x.ItemId))) { alreadyOwned = true; break; }
            // Legacy items prove the reward already exists; preserve their exact instance.
            if (!alreadyOwned)
            {
                if (await placement.WornSlotForAsync(itemId, ct) != new BusinessSlot(BusinessInventorySpace.WornEquipment, 1))
                    throw new InvalidDataException("Title reward is not wearable equipment.");
                var template = await rules.RewardTemplateAsync(itemId, ct) ?? throw new InvalidDataException("Title template is unavailable.");
                if (!template.Supported || template.DefaultPeriodDays != 0 || template.AbsoluteExpiration != 0 || template.Placement.Traits != ItemTraits.Equipment)
                    throw new InvalidDataException("Title reward must be supported permanent non-pile equipment.");
                var snapshot = new ItemSnapshot { ItemId = itemId, Count = 1, LegacyKind = template.Placement.ItemKind,
                    Type = template.Placement.Traits, IsPile = false, Durability = template.Durability };
                var record = await session.CreateAsync(snapshot with { SealFlag = 0 }, ct);
                await session.PutAsync(Book(character, slot.Category), slot.Index, record, ct);
            }
            await events.EnqueueAsync(new TitleUnlocked(character, achievementId, slot.Category, slot.Index, itemId), ct);
        }
    }
    private TitleBookSlotDefinition? Find(int category, int index) => rules.Catalog().FirstOrDefault(x => x.Category == category)?.Slots.FirstOrDefault(x => x.Index == index && x.SlotType >= 0);
    private async ValueTask<bool> UsableAsync(ItemSnapshot item, BusinessSlot slot, CancellationToken ct) =>
        !item.IsPile && item.EquipmentLockId == 0 && item.SortLockFlag == 0 && item.ExpireTime <= 0
        && await placement.WornSlotForAsync(item.ItemId, ct) is { Space: BusinessInventorySpace.WornEquipment, Index: 1 } worn
        && (slot.Space == BusinessInventorySpace.CarriedEquipment || slot == worn);
    private async ValueTask<bool> QualifiedAsync(CharacterId character, TitleBookSlotDefinition definition, CancellationToken ct) =>
        definition.QuestId <= 0 || await persistence.IsCompletedAsync(character, definition.QuestId, ct)
        || (await characterFacts.ReadAsync(character, ct)).ClearedQuests.Contains(definition.QuestId);
    public async ValueTask<TitleBookMutationResult> StoreAsync(CharacterId character, BusinessSlot source,
        int itemId, int category, int index, CancellationToken ct)
    {
        TitleBookMutationResult Fail(string reason) => new(false, reason, category, index, source);
        var definition = Find(category, index);
        if (definition is null || !definition.AllowsItem(itemId)) return Fail("InvalidTitleSlot");
        if (!await QualifiedAsync(character, definition, ct)) return Fail("TitleLocked");
        if (source.Index < 0 || source.Space is not (BusinessInventorySpace.CarriedEquipment or BusinessInventorySpace.WornEquipment)) return Fail("InvalidSource");
        var game = mapping.ToGameSlot(source); var container = ContainerId.ForCharacter(character, game.Space);
        if (source.Space == BusinessInventorySpace.CarriedEquipment && source.Index >= await capacity.OpenSlotCountAsync(container, source.Space, ct)) return Fail("InvalidSource");
        var session = Session; var current = await session.GetAsync(container, game.Index, ct);
        if (current is null || current.ItemId != itemId || !await UsableAsync(current.Snapshot, source, ct)) return Fail("InvalidItem");
        var target = await session.GetAsync(Book(character, category), index, ct);
        if (target is not null && (source.Space == BusinessInventorySpace.WornEquipment || !await UsableAsync(target.Snapshot, source, ct))) return Fail("OccupiedTitleSlot");
        var moving = await session.TakeAsync(current, null, ct);
        if (target is not null)
        {
            var exchanged = await session.TakeAsync(target, null, ct);
            await session.PutAsync(container, game.Index, exchanged, ct);
        }
        await session.PutAsync(Book(character, category), index, moving, ct);
        await events.EnqueueAsync(new TitleStored(character, category, index, source), ct);
        return new(true, null, category, index, source);
    }
    public async ValueTask<TitleBookMutationResult> WithdrawAsync(CharacterId character, BusinessSlot destination,
        int itemId, int category, int index, CancellationToken ct)
    {
        TitleBookMutationResult Fail(string reason) => new(false, reason, category, index, destination);
        var definition = Find(category, index);
        if (definition is null || !definition.AllowsItem(itemId)) return Fail("InvalidTitleSlot");
        if (!await QualifiedAsync(character, definition, ct)) return Fail("TitleLocked");
        if (definition.QuestId > 0 && destination.Space != BusinessInventorySpace.WornEquipment) return Fail("MustEquipTitle");
        if (destination.Index < 0 || destination.Space is not (BusinessInventorySpace.CarriedEquipment or BusinessInventorySpace.WornEquipment)) return Fail("InvalidDestination");
        var game = mapping.ToGameSlot(destination); var container = ContainerId.ForCharacter(character, game.Space);
        if (destination.Space == BusinessInventorySpace.CarriedEquipment && destination.Index >= await capacity.OpenSlotCountAsync(container, destination.Space, ct)) return Fail("InvalidDestination");
        var session = Session; var current = await session.GetAsync(Book(character, category), index, ct);
        if (current is null || current.ItemId != itemId || !await UsableAsync(current.Snapshot, destination, ct)) return Fail("InvalidItem");
        if (destination.Space == BusinessInventorySpace.WornEquipment && !await wear.CanWearAsync(character, itemId, ct)) return Fail("NotWearable");
        var target = await session.GetAsync(container, game.Index, ct);
        if (target is not null) return Fail("OccupiedDestination");
        var moving = await session.TakeAsync(current, null, ct);
        await session.PutAsync(container, game.Index, moving, ct);
        await events.EnqueueAsync(new TitleWithdrawn(character, category, index, destination), ct);
        return new(true, null, category, index, destination);
    }
}
