using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Application.Characters;

namespace Dfo.Server.Application.Achievements;

internal sealed class TitleBookDomainService(ITitleBookRuleService rules, IAchievementPersistence persistence,
    IAchievementRuleService achievementRules,
    ITitleBookPersistence titlePersistence, IAchievementCharacterFactsDomainService characterFacts,
    IItemDomainStore items, IDomainTransactionAccessor transactions, IItemPlacementRuleService placement, IInventoryCapacityDomainService capacity, IEventService events,
    IEquipmentWearDomainService wear, IItemMirrorDomainService mirrors) : ITitleBookDomainService, IDomainEventSubscriber<AchievementCompleted>
{
    private static ContainerId Book(CharacterId character, int category) =>
        ContainerId.ForCharacter(character, (InventoryContainerType)((int)InventoryContainerType.TitleBookGeneral + category));
    private IItemDomainStoreSession Session => items.SessionIn(transactions.Current
        ?? throw new InvalidOperationException("Title mutations require a transaction."));
    public async ValueTask<bool> SetAppearanceAsync(CharacterId character, int appearanceItemId, CancellationToken ct)
    {
        if (appearanceItemId < 0) return false;
        var session = Session;
        var worn = await session.GetAsync(ContainerId.ForCharacter(character, InventoryContainerType.Equipment), 13, ct);
        if (worn is null || await placement.WornSlotForAsync(worn.ItemId, ct) != new BusinessSlot(BusinessInventorySpace.WornEquipment, 1)) return false;
        if (appearanceItemId > 0 && appearanceItemId != worn.ItemId)
        {
            if (await placement.WornSlotForAsync(appearanceItemId, ct) != new BusinessSlot(BusinessInventorySpace.WornEquipment, 1)) return false;
            var owned = false;
            foreach (var type in Enumerable.Range((int)InventoryContainerType.TitleBookGeneral, 5).Select(x => (InventoryContainerType)x).Append(InventoryContainerType.Main))
                if ((await session.GetAllAsync(ContainerId.ForCharacter(character, type), ct)).Any(x => x.ItemId == appearanceItemId)) { owned = true; break; }
            if (!owned) return false;
        }
        int? appearance = appearanceItemId == 0 || appearanceItemId == worn.ItemId ? null : appearanceItemId;
        if (worn.Snapshot.AppearanceItemId == appearance) return true;
        await session.UpdateAsync(worn, x => x with { AppearanceItemId = appearance }, ct);
        await events.EnqueueAsync(new TitleAppearanceChanged(character), ct);
        return true;
    }
    public async ValueTask<IReadOnlyList<CharacterTitleBookCategoryState>> GetAsync(CharacterId character, CancellationToken ct)
    {
        var (rows, equippedSource) = await ReadClientProjectionAsync(character, ct);
        var source = rows.SingleOrDefault(x => equippedSource is not null && x.Item.ItemUid == equippedSource);
        return rules.Catalog().Select(category => new CharacterTitleBookCategoryState((byte)category.Category, 0, 0,
            rows.Where(x => x.Category == category.Category).Select(x => new CharacterTitleBookEntryState(
                checked((ushort)x.Index), x.Item.ItemId, x.Item.InstanceValue, x.Item.Attribute, x.Item.Durability,
                x.Item.SealFlag, x.Item.EnchantCardId, x.Item.EnchantUpgradeCount, x.Item.AmplifyType, x.Item.AmplifyValue)).ToArray())
            { MirrorProjectionEnabled = true, WearSource = source is null ? null : new(source.Category, source.Index, source.Item.ItemId) }).ToArray();
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
    public async ValueTask<IReadOnlyList<InventoryItemState>> GetItemsAsync(CharacterId character, int category, CancellationToken ct)
    {
        if (category is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(category));
        var (rows, _) = await ReadClientProjectionAsync(character, ct);
        return rows.Where(x => x.Category == category)
            .Select(x => new InventoryItemState(x.Item.ItemUid, x.Index, x.Item.ToCore())).ToArray();
    }
    // The book projects canonical ownership; the explicit source identifies the worn mirror.
    private async ValueTask<(IReadOnlyList<StoredTitleBookItem> Rows, ItemIdentity? EquippedSource)> ReadClientProjectionAsync(CharacterId character, CancellationToken ct)
    {
        var rows = await titlePersistence.ReadTitlesAsync(character, ct);
        var source = await titlePersistence.ReadWearSourceAsync(character, ct);
        return (rows, source);
    }
    private async ValueTask<IReadOnlySet<int>> GetUnlockedAchievementIdsAsync(CharacterId character, CancellationToken ct)
    {
        var unlocked = (await titlePersistence.ReadUnlockedSlotsAsync(character, ct)).ToHashSet();
        return rules.ActivatedAchievements(unlocked);
    }
    private async ValueTask UnlockAsync(CharacterId character, int achievementId, CancellationToken ct)
    {
        var unlocked = (await titlePersistence.ReadUnlockedSlotsAsync(character, ct)).ToHashSet();
        var activated = rules.ActivatedAchievements(unlocked);
        if (activated.Contains(achievementId)) return;
        var definition = achievementRules.Catalog().FirstOrDefault(x => x.Id == achievementId);
        if (definition is null || !await persistence.IsCompletedAsync(character, achievementId, ct)) return;
        if (definition.Condition.Type is "clear quest" or "clearquest")
        {
            var facts = await characterFacts.ReadAsync(character, ct);
            var confirmed = achievementRules.CompletedFromFacts(await persistence.ReadAsync(character, ct),
                facts.ClearedQuests, activated);
            if (!confirmed.Contains(achievementId)) return; // Reject stale pre-refactor completion events.
        }
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
    private async ValueTask<bool> UsableAsync(ItemSnapshot item, BusinessSlot slot, CancellationToken ct, bool requirePermanent = true) =>
        !item.IsPile && item.EquipmentLockId == 0 && item.SortLockFlag == 0 && (!requirePermanent || item.Lifetime.IsPermanent)
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
        var game = A21InventoryLayout.ToGameSlot(source); var container = ContainerId.ForCharacter(character, game.Space);
        if (source.Space == BusinessInventorySpace.CarriedEquipment && source.Index >= await capacity.OpenSlotCountAsync(container, source.Space, ct)) return Fail("InvalidSource");
        var session = Session; var current = await session.GetAsync(container, game.Index, ct);
        if (current is null || current.ItemId != itemId || !await UsableAsync(current.Snapshot, source, ct)) return Fail("InvalidItem");
        var target = await session.GetAsync(Book(character, category), index, ct);
        if (current.Snapshot.IsMirror)
        {
            if (target is null || current.Snapshot.MirrorSourceUid != target.ItemUid) return Fail("InvalidMirrorSource");
            await mirrors.RemoveAsync(session, current, ct, refreshCarriedInventory: false);
            await events.EnqueueAsync(new TitleStored(character, category, index, source), ct);
            return new(true, null, category, index, source);
        }
        if (target is not null && await IsEquippedSourceAsync(character, target.ItemUid, ct)) return Fail("TitleEquipped");
        if (target is not null && (source.Space == BusinessInventorySpace.WornEquipment || !await UsableAsync(target.Snapshot, source, ct))) return Fail("OccupiedTitleSlot");
        var moving = await session.TakeAsync(current, null, ct);
        if (target is not null)
        {
            await RemoveEquippedMirrorOfAsync(character, target.ItemUid, ct);
            var exchanged = await session.TakeAsync(target, null, ct);
            await session.PutAsync(container, game.Index, exchanged, ct);
        }
        await session.PutAsync(Book(character, category), index, moving, ct);
        await events.EnqueueAsync(new TitleStored(character, category, index, source), ct);
        return new(true, null, category, index, source);
    }
    private async ValueTask RemoveEquippedMirrorOfAsync(CharacterId character, ItemIdentity? source, CancellationToken ct)
    {
        if (source is null) return;
        foreach (var item in await Session.GetAllAsync(ContainerId.ForCharacter(character, InventoryContainerType.Equipment), ct))
            if (item.Snapshot.IsMirror && item.Snapshot.MirrorSourceUid == source) await mirrors.RemoveAsync(Session, item, ct, refreshCarriedInventory: false);
    }
    private async ValueTask<bool> IsEquippedSourceAsync(CharacterId character, ItemIdentity? source, CancellationToken ct)
    {
        if (source is null) return false;
        var worn = await Session.GetAsync(ContainerId.ForCharacter(character, InventoryContainerType.Equipment), 13, ct);
        return worn is not null && worn.Snapshot.IsMirror && worn.Snapshot.MirrorSourceUid == source;
    }
    public async ValueTask<TitleBookMutationResult> EquipCarriedTitleReplacingMirrorAsync(CharacterId character, BusinessSlot source,
        int itemId, int category, int index, CancellationToken ct)
    {
        var destination = new BusinessSlot(BusinessInventorySpace.WornEquipment, 1);
        TitleBookMutationResult Fail(string reason) => new(false, reason, category, index, destination);
        if (Find(category, index) is null || source.Space != BusinessInventorySpace.CarriedEquipment || source.Index < 0)
            return Fail("InvalidSource");
        var bag = ContainerId.ForCharacter(character, InventoryContainerType.Main);
        if (source.Index >= await capacity.OpenSlotCountAsync(bag, source.Space, ct)) return Fail("InvalidSource");
        var session = Session;
        var piece = await session.GetAsync(bag, A21InventoryLayout.ToGameSlot(source).Index, ct);
        var original = await session.GetAsync(Book(character, category), index, ct);
        var worn = await session.GetAsync(ContainerId.ForCharacter(character, InventoryContainerType.Equipment), 13, ct);
        if (original is null || worn?.Snapshot.IsMirror != true || worn.Snapshot.MirrorSourceUid != original.ItemUid)
            return Fail("InvalidMirrorSource");
        if (piece is null || piece.ItemId != itemId || piece.Snapshot.IsMirror || !await UsableAsync(piece.Snapshot, destination, ct, requirePermanent: false)
            || !await wear.CanWearAsync(character, itemId, ct)) return Fail("NotWearable");
        // Every refusal precedes deletion. Both ends and the mirror settle in this command's transaction.
        var prepared = wear.UnsealForEquip(piece.Snapshot);
        if (prepared.SealFlag != piece.Snapshot.SealFlag) piece = await session.UpdateAsync(piece, _ => prepared, ct);
        await mirrors.RemoveAsync(session, worn, ct, refreshCarriedInventory: false);
        var moving = await session.TakeAsync(piece, null, ct);
        await session.PutAsync(ContainerId.ForCharacter(character, InventoryContainerType.Equipment), 13, moving, ct);
        await events.EnqueueAsync(new TitleWithdrawn(character, category, index, destination) { CarriedInventoryChanged = true }, ct);
        return new(true, null, category, index, destination) { CarriedInventoryChanged = true };
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
        var game = A21InventoryLayout.ToGameSlot(destination); var container = ContainerId.ForCharacter(character, game.Space);
        if (destination.Space == BusinessInventorySpace.CarriedEquipment && destination.Index >= await capacity.OpenSlotCountAsync(container, destination.Space, ct)) return Fail("InvalidDestination");
        var session = Session; var current = await session.GetAsync(Book(character, category), index, ct);
        if (current is null || current.ItemId != itemId || !await UsableAsync(current.Snapshot, destination, ct)) return Fail("InvalidItem");
        if (destination.Space == BusinessInventorySpace.WornEquipment && !await wear.CanWearAsync(character, itemId, ct)) return Fail("NotWearable");
        var target = await session.GetAsync(container, game.Index, ct);
        if (destination.Space == BusinessInventorySpace.WornEquipment)
        {
            if (target is not null && target.Snapshot.IsMirror && target.Snapshot.MirrorSourceUid == current.ItemUid)
            {
                await events.EnqueueAsync(new TitleWithdrawn(character, category, index, destination), ct);
                return new(true, null, category, index, destination);
            }
            int? free = null;
            var bag = ContainerId.ForCharacter(character, InventoryContainerType.Main);
            if (target is not null && !target.Snapshot.IsMirror)
            {
                var open = await capacity.OpenSlotCountAsync(bag, BusinessInventorySpace.CarriedEquipment, ct);
                for (var i = 0; i < open; i++)
                {
                    var candidate = A21InventoryLayout.ToGameSlot(new(BusinessInventorySpace.CarriedEquipment, i));
                    if (await session.GetAsync(bag, candidate.Index, ct) is null) { free = candidate.Index; break; }
                }
                if (free is null) return Fail("InventoryFull");
            }
            if (target is not null && !await mirrors.RemoveAsync(session, target, ct, refreshCarriedInventory: false))
                await session.PutAsync(bag, free!.Value, await session.TakeAsync(target, null, ct), ct);
            var prepared = wear.UnsealForEquip(current.Snapshot);
            if (prepared.SealFlag != current.Snapshot.SealFlag)
                current = await session.UpdateAsync(current, _ => prepared, ct);
            var mirror = await session.CreateAsync(current.Snapshot with
            {
                ItemUid = null, IsMirror = true, MirrorSourceUid = current.ItemUid,
                AppearanceItemId = target is not null && target.Snapshot.IsMirror && target.Snapshot.MirrorSourceUid == current.ItemUid
                    ? target.Snapshot.AppearanceItemId : null,
            }, ct);
            await session.PutAsync(container, game.Index, mirror, ct);
        }
        else
        {
            if (await IsEquippedSourceAsync(character, current.ItemUid, ct)) return Fail("TitleEquipped");
            if (target is not null) return Fail("OccupiedDestination");
            var moving = await session.TakeAsync(current, null, ct);
            await session.PutAsync(container, game.Index, moving, ct);
        }
        var carriedChanged = destination.Space == BusinessInventorySpace.WornEquipment && target is not null && !target.Snapshot.IsMirror;
        await events.EnqueueAsync(new TitleWithdrawn(character, category, index, destination) { CarriedInventoryChanged = carriedChanged }, ct);
        return new(true, null, category, index, destination) { CarriedInventoryChanged = carriedChanged };
    }
}
