using Dfo.Server.Abstractions.Effects;
using System.Security.Cryptography;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Inventory;

namespace Dfo.Server.Application.Marriage;

internal sealed partial class HousingOperations(HousingState state, IMarriageHousingContentSource content,
    IBusinessInventorySlotDomainService inventory,
    IItemDeliveryRuleService templates, IItemLifecycleDomainService lifecycle, IMailboxDomainService mail, IDailyResetRuleService days, TimeProvider clock,
    Dfo.Server.Abstractions.Characters.ICharacterDomainService characters,
    Dfo.Server.Abstractions.Accounts.IAccountWalletDomainService wallets,
    INpcShopDefinitionSource shops, IItemDeliveryDomainService delivery)
{
    public ValueTask<ItemSnapshot> TemplateAsync(int itemId, CancellationToken ct) => templates.TemplateAsync(itemId, ct);
    public MarriageHousingContent Rules => content.Load();
    public long Period => days.PeriodStart(clock.GetUtcNow()).ToUnixTimeMilliseconds();

    public async ValueTask<HousingResult> ExecuteAsync(SelectedSessionIdentity actor, MarriageRelation relation,
        HousingSnapshot room, HousingRequest request, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandActive(actor);
        if (!state.IsInside(actor, room)) return new(-4);
        var now = clock.GetUtcNow();
        switch (request.Action)
        {
            case HousingAction.Storage:
                return new(state.Edit(relation.Id, actor, request.Count == 1) ? 0 : -4) { Snapshot = room };
            case HousingAction.Move:
                if (!state.CanEdit(relation.Id, actor)) return new(-20);
                var moved = Move(room, request);
                if (moved is null) return new(-1);
                footprint.DemandActive(actor);
                await state.SaveAsync(moved, ct).ConfigureAwait(false);
                return new(0) { Snapshot = moved };
            case HousingAction.Delete:
                if (!state.CanEdit(relation.Id, actor) || request.Count <= 0) return new(-1);
                var item = room.Storage.SingleOrDefault(x => x.Slot == request.Slot);
                if (item is null || item.ItemId != request.ItemId || item.Count < request.Count) return new(-1);
                var storage = room.Storage.Where(x => x.Slot != item.Slot).ToList();
                if (item.Count > request.Count) storage.Add(item with { Count = item.Count - request.Count });
                var deleted = room with { Storage = storage };
                footprint.DemandActive(actor);
                await state.SaveAsync(deleted, ct).ConfigureAwait(false);
                return new(0) { Snapshot = deleted };
            case HousingAction.Plant:
                if (room.Tree is not null) return new(-4);
                if (!Rules.Seeds.TryGetValue(request.ItemId, out var kind)) return new(-1);
                var planting = await ConsumeAsync(actor, request.Slot, request.ItemId, footprint, ct).ConfigureAwait(false);
                if (planting is null) return new(-1);
                var planted = room with { Tree = new(Guid.NewGuid(), kind, now, now.AddDays(30), 0, 0, Period, 0, 1) };
                footprint.DemandActive(actor);
                await state.SaveAsync(planted, ct).ConfigureAwait(false);
                return new(0) { Snapshot = planted, ConsumedSlot = planting.Value.Slot, Remaining = planting.Value.Remaining };
            case HousingAction.Water:
                if (room.Tree is not { } tree) return new(-4);
                if (tree.ExpiresAt <= now) return new(-11);
                if (tree.Growth >= 30) return new(-9);
                var waters = tree.WaterPeriod == Period ? tree.Waters : 0;
                if (waters >= 2) return new(-6);
                if (!Rules.Water.TryGetValue(request.ItemId, out var growth)) return new(-1);
                var watering = await ConsumeAsync(actor, request.Slot, request.ItemId, footprint, ct).ConfigureAwait(false);
                if (watering is null) return new(-1);
                var watered = room with { Tree = tree with { Growth = Math.Min(30, tree.Growth + growth), Waters = waters + 1, WaterPeriod = Period, Revision = tree.Revision + 1 } };
                footprint.DemandActive(actor);
                await state.SaveAsync(watered, ct).ConfigureAwait(false);
                return new(0) { Snapshot = watered, ConsumedSlot = watering.Value.Slot, Remaining = watering.Value.Remaining };
            case HousingAction.Harvest:
                if (room.Tree is not { } mature) return new(-6);
                if (mature.ExpiresAt <= now) return new(-7);
                if (mature.Growth < 30) return new(-6);
                var rewards = new[] { relation.Proposer.CharacterId, relation.Recipient.CharacterId }.Select(character =>
                {
                    var draw = RandomNumberGenerator.GetInt32(Rules.Rewards.Sum(x => x.Weight));
                    foreach (var reward in Rules.Rewards) { if (draw < reward.Weight) return new HousingReward(character, reward.ItemId, reward.Count); draw -= reward.Weight; }
                    throw new InvalidOperationException("Invalid tree reward weights.");
                }).ToArray();
                footprint.DemandActive(actor);
                if (!await state.RecordHarvestAsync(mature.PlantingId, mature.HarvestRound, rewards, ct).ConfigureAwait(false)) return new(-6);
                foreach (var reward in rewards)
                {
                    footprint.DemandActive(actor);
                    await mail.SendAsync(MailBuilder.FromSystem(reward.Character).WithText("爱情树的礼物", "感谢你们悉心培育爱情树，这是本轮收获的礼物。").AddStackable(reward.ItemId, reward.Count).Build(), ct).ConfigureAwait(false);
                }
                var harvested = room with { Tree = mature with { Growth = 0, HarvestRound = mature.HarvestRound + 1, Revision = mature.Revision + 1 } };
                footprint.DemandActive(actor);
                await state.SaveAsync(harvested, ct).ConfigureAwait(false);
                return new(0) { Snapshot = harvested, Rewards = rewards };
            case HousingAction.Clear:
                if (room.Tree is null) return new(-1);
                var cleared = room with { Tree = null };
                footprint.DemandActive(actor);
                await state.SaveAsync(cleared, ct).ConfigureAwait(false);
                return new(0) { Snapshot = cleared };
            default: return new(-1);
        }
    }

    public async ValueTask<(BusinessSlot Slot, ItemSnapshot? Remaining)?> ConsumeAsync(SelectedSessionIdentity actor, int wireSlot, int itemId, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandActive(actor);
        BusinessSlot slot;
        try { slot = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, wireSlot)); }
        catch (InventorySlotMappingException) { return null; }
        if (!slot.Space.IsCarriedItemSpace()) return null;
        var bound = inventory.Find(actor.CharacterId, slot);
        var item = bound is null ? null : await bound.Container.GetAsync(bound.Slot.Index, ct).ConfigureAwait(false);
        if (item is null || item.ItemId != itemId || !item.Snapshot.IsPile || !item.Snapshot.Lifetime.IsPermanent) return null;
        footprint.RecordInventorySlots(actor, [slot]);
        await ItemContainerOperations.ConsumeAsync(bound!.Container, item, 1, lifecycle, ct).ConfigureAwait(false);
        return (slot, (await bound.Container.GetAsync(bound.Slot.Index, ct).ConfigureAwait(false))?.Snapshot);
    }

    public HousingSnapshot? AddFurniture(HousingSnapshot room, int itemId, int count)
    {
        if (count <= 0 || !Rules.Furniture.ContainsKey(itemId)) return null;
        var stored = room.Storage.ToList(); var item = stored.SingleOrDefault(x => x.ItemId == itemId);
        if (item is not null)
        {
            if ((long)item.Count + count > 1000) return null;
            stored.Remove(item); stored.Add(item with { Count = item.Count + count });
        }
        else
        {
            var slot = Enumerable.Range(0, 40).FirstOrDefault(i => stored.All(x => x.Slot != i), -1);
            if (slot < 0 || count > 1000) return null;
            stored.Add(new(slot, itemId, count));
        }
        return room with { Storage = stored };
    }
    private HousingSnapshot? Move(HousingSnapshot room, HousingRequest request)
    {
        if (request.Source is not { } a || request.Destination is not { } b || a == b
            || a.Kind > 1 || b.Kind > 1 || a.Slot < 0 || b.Slot < 0 || a.Slot >= (a.Kind == 1 ? 40 : 28) || b.Slot >= (b.Kind == 1 ? 40 : 28)) return null;
        var stored = room.Storage.ToList(); var placed = room.Placements.ToList();
        if (a.Kind == 1 && b.Kind == 1)
        {
            var source = stored.SingleOrDefault(x => x.Slot == a.Slot);
            var destination = stored.SingleOrDefault(x => x.Slot == b.Slot);
            if (source is null || source.ItemId != a.ItemId || (destination?.ItemId ?? 0) != b.ItemId) return null;
            stored.Remove(source); if (destination is not null) stored.Remove(destination);
            if (destination?.ItemId == source.ItemId)
            { if (source.Count + destination.Count > 1000) return null; stored.Add(source with { Slot = b.Slot, Count = source.Count + destination.Count }); }
            else { stored.Add(source with { Slot = b.Slot }); if (destination is not null) stored.Add(destination with { Slot = a.Slot }); }
        }
        else if (a.Kind == 1 && b.Kind == 0)
        {
            var source = stored.SingleOrDefault(x => x.Slot == a.Slot);
            if (source is null || source.ItemId != a.ItemId || !Rules.Furniture.TryGetValue(source.ItemId, out var rule) || !rule.Slots.Contains(b.Slot) || placed.Any(x => x.Slot == b.Slot) || b.ItemId != source.ItemId) return null;
            // Window/curtain exclusion is the approved phase-one server rule; native anchor pairing remains unverified.
            if (b.Slot is 3 or 14 or 15 && placed.Any(x => x.Slot is 25 or 26 or 27)
                || b.Slot is 25 or 26 or 27 && placed.Any(x => x.Slot is 3 or 14 or 15)) return null;
            stored.Remove(source); if (source.Count > 1) stored.Add(source with { Count = source.Count - 1 });
            placed.Add(new(b.Slot, source.ItemId));
        }
        else if (a.Kind == 0 && b.Kind == 1)
        {
            var source = placed.SingleOrDefault(x => x.Slot == a.Slot);
            var destination = stored.SingleOrDefault(x => x.Slot == b.Slot);
            if (source is null || source.ItemId != a.ItemId || !Rules.Furniture.TryGetValue(source.ItemId, out var rule) || !rule.CanRecall
                || b.ItemId != source.ItemId || destination is not null && (destination.ItemId != source.ItemId || destination.Count >= 1000)) return null;
            placed.Remove(source); if (destination is not null) stored.Remove(destination);
            stored.Add(new(b.Slot, source.ItemId, (destination?.Count ?? 0) + 1));
        }
        else return null;
        return room with { Storage = stored, Placements = placed };
    }
}
