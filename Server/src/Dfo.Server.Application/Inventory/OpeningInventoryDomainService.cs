using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class OpeningInventoryDomainService(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers, ICharacterTable characters,
    ICharacterContainerDomainService containers, IItemArrivalRuleService arrivals,
    IItemDomainStore store, IDomainTransactionAccessor transactions,
    ICharacterInventoryEntryDomainService inventory, IAccountPremiumTable premiums,
    ICharacterCreatureTable creatures,
    ICharacterDomainService characterDomain, IItemUseJudgementService useRules,
    ICharacterItemCooldownDomainService cooldowns) : IOpeningInventoryDomainService
{
    public async ValueTask<ContainerSlotItem> SourceAsync(SelectedSessionIdentity session, int slot, DateTimeOffset now, CancellationToken ct)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Opening requires a transaction.");
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId
            || !await characters.BelongsToAsync(session.CharacterId, session.AccountId, ct).ConfigureAwait(false))
            throw new OpeningRejectedException(OpeningRejection.NoSession, "No current owned selection.");
        var bag = containers.FindContainer(session.CharacterId, InventoryContainerType.Main)
            ?? throw new OpeningRejectedException(OpeningRejection.NoSession, "No main container.");
        var item = await bag.ReadSlotAsync(slot, ct).ConfigureAwait(false);
        if (item is null || !item.IsPile || item.Count <= 0 || slot < 3
            || item.Core.ExpireTime > 0 && item.Core.ExpireTime <= now.ToUnixTimeSeconds())
            throw new OpeningRejectedException(OpeningRejection.WrongItem, "Missing, expired or nonstackable source.");
        var usability = await characterDomain.Get(session.CharacterId).CanUseAsync(item.ItemId, ct).ConfigureAwait(false);
        if (usability != ItemUsability.Allowed)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, $"Opening source is unusable: {usability}.");
        return item;
    }

    public async ValueTask<OpeningInventoryReceipt> SettleAsync(SelectedSessionIdentity session,
        IReadOnlyList<OpeningSlotConsumption> consumed, ImmutableArray<OpeningPreparedGrant> grants, DateTimeOffset now, CancellationToken ct)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("Opening requires a transaction.");
        if (consumed.Count == 0) throw new ArgumentException("Opening requires a source consumption.", nameof(consumed));
        var useRule = await useRules.RuleForAsync(consumed[0].ItemId, ct).ConfigureAwait(false);
        if (useRule is not { IsReadable: true }) throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Unusable source declaration.");
        var bags = new Dictionary<InventoryContainerType, IItemContainer>();
        var standing = new Dictionary<InventoryContainerType, Dictionary<int, ContainerSlotItem>>();
        foreach (var type in (InventoryContainerType[])[InventoryContainerType.Main, InventoryContainerType.Avatar, InventoryContainerType.Pet])
            if (containers.FindContainer(session.CharacterId, type) is { } bag)
            {
                bags[type] = bag;
                standing[type] = (await bag.ReadAllAsync(ct).ConfigureAwait(false)).ToDictionary(item => item.SlotIndex);
            }
        if (!standing.TryGetValue(InventoryContainerType.Main, out var main))
            throw new OpeningRejectedException(OpeningRejection.NoSession, "No main container.");
        var writes = new Dictionary<(InventoryContainerType Bag, int Slot), (InventoryItemCoreState? Core, InventoryItemCreationFacts? Facts)>();
        foreach (var consume in consumed)
        {
            if (consume.Slot < 3 || consume.Count <= 0 || !main.TryGetValue(consume.Slot, out var item)
                || !item.IsPile || item.ItemId != consume.ItemId || item.Count < consume.Count
                || item.Core.ExpireTime > 0 && item.Core.ExpireTime <= now.ToUnixTimeSeconds())
                throw new OpeningRejectedException(OpeningRejection.MissingMaterial, "Source or material changed or insufficient.");
            var left = item.Count - consume.Count;
            if (left == 0) { main.Remove(consume.Slot); writes[(InventoryContainerType.Main, consume.Slot)] = (null, null); }
            else
            {
                var changed = item with { Core = item.Core with { Value = left } };
                main[consume.Slot] = changed;
                writes[(InventoryContainerType.Main, consume.Slot)] = (changed.Core, null);
            }
        }
        var displayed = ImmutableArray.CreateBuilder<OpeningRewardDisplay>();
        var oldPremiums = grants.Any(g => g.Item.PremiumType > 0)
            ? await premiums.ReadAsync(session.AccountId, ct).ConfigureAwait(false) : [];
        var premiumState = oldPremiums.ToDictionary(p => p.PremiumType, p => p.ExpiresAtUnixSeconds);
        var changedPremiums = new Dictionary<int, long>();
        var identity = (long)Random.Shared.Next(1, int.MaxValue);
        foreach (var grant in grants)
        {
            if (grant.Item.PremiumType > 0)
            {
                var typeId = grant.Item.PremiumType;
                var seconds = checked((long)grant.Item.PremiumDays * grant.Reward.Count * 86400);
                if (seconds <= 0) throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Invalid premium duration.");
                var expiry = checked(Math.Max(now.ToUnixTimeSeconds(), premiumState.GetValueOrDefault(typeId)) + seconds);
                premiumState[typeId] = changedPremiums[typeId] = expiry;
                displayed.Add(new(-1, grant.Reward.ItemId, grant.Reward.Count, 0, 0, 0));
                continue;
            }
            var placement = grant.Item.Placement;
            var type = placement.ItemKind switch { 8 => InventoryContainerType.Avatar, 5 or 6 or 7 => InventoryContainerType.Pet, _ => InventoryContainerType.Main };
            if (!standing.TryGetValue(type, out var slots))
                throw new OpeningRejectedException(OpeningRejection.NoSession, $"No {type} container.");
            var baseline = StackableItemCore.Create(placement.ItemKind, grant.Item.ItemId, 0) with
            {
                Attribute = grant.Reward.Ability, Durability = grant.Item.Durability,
                SealFlag = placement.WorldClass == ItemWorldClass.Creature ? (byte)0 : grant.Item.Sealed ? (byte)1 : (byte)0,
                ExpireTime = placement.WorldClass == ItemWorldClass.Avatar ? 0 : grant.Expiration,
            };
            var remaining = grant.Reward.Count;
            while (remaining > 0)
            {
                var piles = slots.Values.Where(item => item.IsPile && item.ItemId == grant.Item.ItemId
                    && item.Core with { Value = 0 } == baseline).Select(item => new ItemPile(item.SlotIndex, item.Count)).ToArray();
                var arrival = arrivals.ForGrant(piles, remaining, placement, slots.Keys.ToHashSet());
                int slot, added; InventoryItemCoreState core; InventoryItemCreationFacts? facts = null;
                if (arrival is ItemArrival.OntoExistingPile pile)
                { slot = pile.SlotIndex; added = pile.Added; core = slots[slot].Core with { Value = pile.Total }; }
                else if (arrival is ItemArrival.IntoEmptySlot empty)
                {
                    slot = empty.SlotIndex; added = empty.Count;
                    core = baseline with { Value = placement.WorldClass == ItemWorldClass.Interchangeable ? added : (int)(identity++ % (int.MaxValue - 1)) + 1 };
                    facts = placement.WorldClass switch
                    {
                        ItemWorldClass.Avatar => new(new(grant.Expiration, 0, new byte[30], 0, 0, 0)),
                        ItemWorldClass.Creature => new(Creature: new(100, 0, 0, 0, 0, grant.Item.CreatureStartLevel!.Value, [], 0)),
                        _ => null,
                    };
                }
                else throw new OpeningRejectedException(OpeningRejection.NoRoom, $"No room for {grant.Item.ItemId} in {type}.");
                if (added <= 0 || added > remaining) throw new InvalidOperationException("Nonprogressing grant.");
                remaining -= added;
                slots[slot] = new(slot, core, placement.WorldClass);
                writes[(type, slot)] = (core, facts);
                displayed.Add(new(slot, grant.Reward.ItemId, added, core.Durability, core.Attribute, grant.Expiration));
            }
        }
        foreach (var (key, write) in writes.OrderBy(p => p.Key.Bag).ThenBy(p => p.Key.Slot))
        {
            if (write.Core is null) await bags[key.Bag].ClearAsync(key.Slot, ct).ConfigureAwait(false);
            else if (write.Facts is { } facts) await bags[key.Bag].PutNewAsync(key.Slot, write.Core, facts, ct).ConfigureAwait(false);
            else await bags[key.Bag].PutAsync(key.Slot, write.Core, ct).ConfigureAwait(false);
        }
        await store.SessionIn(transaction).ApplyDecidedWritesAsync(ct).ConfigureAwait(false);
        foreach (var (type, expiry) in changedPremiums)
            if (await premiums.UpsertExpiryAsync(session.AccountId, type, expiry, ct).ConfigureAwait(false) != 1)
                throw new OpeningRejectedException(OpeningRejection.StateChanged, "Premium settlement affected no row.");
        if (useRule.CooldownMilliseconds > 0)
            transaction.Enlist(new object(), () => new OpeningCooldown(sessions, cooldowns, session,
                useRules.CooldownKeyFor(consumed[0].ItemId, useRule), TimeSpan.FromMilliseconds(useRule.CooldownMilliseconds)));
        var snapshot = await inventory.LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var updates = ImmutableArray.CreateBuilder<CommittedInventoryUpdateState>();
        foreach (var group in writes.Where(w => w.Value.Core is not null).GroupBy(w => w.Key.Bag))
        {
            var changed = group.Select(w => w.Key.Slot).ToHashSet();
            var items = snapshot.Containers.Single(b => b.ContainerType == group.Key).Items.Where(i => changed.Contains(i.SlotIndex)).ToArray();
            if (items.Length != changed.Count) throw new InvalidDataException("Committed opening reward missing from projection.");
            updates.Add(new(group.Key, items, now));
        }
        var creaturesChanged = grants.Any(g => g.Item.Placement.WorldClass == ItemWorldClass.Creature);
        ImmutableArray<CharacterPetRuntimeState> pets = [];
        if (creaturesChanged)
        {
            var rows = await creatures.ReadAsync(session.CharacterId, ct).ConfigureAwait(false);
            if (rows.Count > byte.MaxValue) throw new OpeningRejectedException(OpeningRejection.NoRoom, "Creature count cannot fit the protocol.");
            pets = rows.OrderBy(row => row.SortOrder).Select(row => new CharacterPetRuntimeState(row.CreatureKey,
                row.Field04, checked((byte)row.ModeFlag), row.ProgressValue, row.Mode1Field0A, row.Mode1Field0B,
                row.FieldAfterValue, [.. row.CreatureText], checked((byte)row.TailFlag))).ToImmutableArray();
        }
        return new(session, updates.ToImmutable(), writes.Where(w => w.Key.Bag == InventoryContainerType.Main && w.Value.Core is null)
            .Select(w => w.Key.Slot).ToImmutableArray(), displayed.ToImmutable(), now,
            changedPremiums.Select(p => new AccountPremiumEntryState(p.Key, p.Value)).ToImmutableArray(), pets, creaturesChanged);
    }

    private sealed class OpeningCooldown(ISelectedSessionDomainService sessions, ICharacterItemCooldownDomainService cooldowns,
        SelectedSessionIdentity session, ItemCooldownKey key, TimeSpan duration) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct)
        {
            if (sessions.Find(session.AccountId) == session) cooldowns.Start(session.CharacterId, key, duration);
            return ValueTask.CompletedTask;
        }
    }
}
