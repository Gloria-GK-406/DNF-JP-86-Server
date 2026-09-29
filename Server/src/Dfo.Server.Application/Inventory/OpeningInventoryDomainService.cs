using Dfo.Server.Abstractions.Accounts;
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
    ICharacterContainerDomainService containers, ICharacterBusinessInventoryDomainService businessInventories,
    IItemArrivalRuleService arrivals,
    IItemDomainStore store, IDomainTransactionAccessor transactions,
    ICharacterInventoryEntryDomainService inventory, IPremiumServiceDomainService premiums,
    ICharacterCreatureTable creatures,
    ICharacterDomainService characterDomain, IItemUseJudgementService useRules,
    ICharacterItemCooldownDomainService cooldowns) : IOpeningInventoryDomainService
{
    public async ValueTask<ContainerSlotItem> SourceAsync(SelectedSessionIdentity session, BusinessSlot slot, DateTimeOffset now, CancellationToken ct)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Opening requires a transaction.");
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId
            || !await characters.BelongsToAsync(session.CharacterId, session.AccountId, ct).ConfigureAwait(false))
            throw new OpeningRejectedException(OpeningRejection.NoSession, "No current owned selection.");
        var bag = businessInventories.GetStore(session.CharacterId, BusinessInventoryOwnership.StoreOf(slot.Space))
            .FindContainer(slot.Space)
            ?? throw new OpeningRejectedException(OpeningRejection.NoSession, "No source container.");
        var item = await bag.ReadSlotAsync(slot.Index, ct).ConfigureAwait(false);
        if (item is null || !item.IsPile || item.Count <= 0
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
        var sourceSlots = consumed.Select(x => x.Slot).ToArray();
        var spaces = sourceSlots.Select(x => x.Space).Concat(grants
                .Where(x => x.Item.PremiumType == 0 && x.Item.Placement.Destination.Space != BusinessInventorySpace.VirtualResource)
                .Select(x => x.Item.Placement.Destination.Space))
            .Concat(grants.Where(x => x.Item.PremiumType == 0 && ItemSlotRuleService.AllowsQuickSlot(x.Item.Placement.ItemKind))
                .Select(_ => BusinessInventorySpace.QuickSlot)).Distinct().ToArray();
        var bags = new Dictionary<BusinessInventorySpace, IBusinessItemContainer>();
        var standing = new Dictionary<BusinessInventorySpace, Dictionary<int, ContainerSlotItem>>();
        foreach (var space in spaces)
        {
            var bag = businessInventories.GetStore(session.CharacterId, BusinessInventoryOwnership.StoreOf(space))
                .FindContainer(space) ?? throw new OpeningRejectedException(OpeningRejection.NoSession, $"No {space} container.");
            bags[space] = bag;
            standing[space] = (await bag.ReadAllAsync(ct).ConfigureAwait(false)).ToDictionary(item => item.SlotIndex);
        }
        var writes = new Dictionary<BusinessSlot, (InventoryItemCoreState? Core, InventoryItemCreationFacts? Facts)>();
        var virtualWrites = new Dictionary<int, InventoryItemCoreState>();
        var mainStorage = containers.FindContainer(session.CharacterId, InventoryContainerType.Main)
            ?? throw new OpeningRejectedException(OpeningRejection.NoSession, "No main container.");
        var virtualSlots = (await mainStorage.ReadAllAsync(ct).ConfigureAwait(false))
            .Where(item => item.SlotIndex is >= 0 and <= 2).ToDictionary(item => item.SlotIndex);
        for (var index = 0; index < consumed.Count; index++)
        {
            var consume = consumed[index];
            var source = sourceSlots[index];
            var held = standing[source.Space];
            if (consume.Count <= 0 || !held.TryGetValue(source.Index, out var item)
                || !item.IsPile || item.ItemId != consume.ItemId || item.Count < consume.Count
                || item.Core.ExpireTime > 0 && item.Core.ExpireTime <= now.ToUnixTimeSeconds())
                throw new OpeningRejectedException(OpeningRejection.MissingMaterial, "Source or material changed or insufficient.");
            var left = item.Count - consume.Count;
            if (left == 0) { held.Remove(source.Index); writes[source] = (null, null); }
            else
            {
                var changed = item with { Core = item.Core with { Value = left } };
                held[source.Index] = changed;
                writes[source] = (changed.Core, null);
            }
        }
        var displayed = ImmutableArray.CreateBuilder<OpeningRewardDisplay>();
        var changedPremiums = new Dictionary<int, long>();
        var identity = (long)Random.Shared.Next(1, int.MaxValue);
        foreach (var grant in grants)
        {
            if (grant.Item.PremiumType > 0)
            {
                var typeId = grant.Item.PremiumType;
                var duration = TimeSpan.FromTicks(checked(grant.Item.PremiumDuration.Ticks * grant.Reward.Count));
                if (duration <= TimeSpan.Zero) throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Invalid premium duration.");
                var changed = await premiums.For(session.AccountId).ExtendAsync((PremiumServiceType)typeId, duration, ct).ConfigureAwait(false);
                changedPremiums[typeId] = changed.ExpiresAtUnixSeconds;
                displayed.Add(new(null, grant.Reward.ItemId, grant.Reward.Count, 0, 0, 0));
                continue;
            }
            var placement = grant.Item.Placement;
            if (placement.Destination.Space == BusinessInventorySpace.VirtualResource)
            {
                if (placement.Destination.FirstIndex != placement.Destination.LastIndex
                    || placement.Destination.FirstIndex is < 0 or > 2
                    || mainStorage.Type != InventoryContainerType.Main)
                    throw new OpeningRejectedException(
                        OpeningRejection.UnsupportedContent, "Invalid virtual reward destination.");
                var slot = placement.Destination.FirstIndex;
                InventoryItemCoreState core;
                if (virtualSlots.TryGetValue(slot, out var existing))
                {
                    if (!existing.IsPile || existing.ItemId != grant.Item.ItemId)
                        throw new OpeningRejectedException(
                            OpeningRejection.UnsupportedContent, "Virtual reward slot is occupied.");
                    core = existing.Core with {
                        Value = checked(existing.Count + grant.Reward.Count) };
                }
                else
                {
                    core = StackableItemCore.Create(
                        placement.ItemKind, grant.Item.ItemId, grant.Reward.Count);
                }
                virtualSlots[slot] = new(slot, core, ItemWorldClass.Interchangeable);
                virtualWrites[slot] = core;
                displayed.Add(new(new(BusinessInventorySpace.VirtualResource, slot), grant.Reward.ItemId, grant.Reward.Count, 0, 0, 0));
                continue;
            }
            var space = placement.Destination.Space;
            if (!standing.ContainsKey(space))
                throw new OpeningRejectedException(OpeningRejection.NoSession, $"No {space} container.");
            var baseline = StackableItemCore.Create(placement.ItemKind, grant.Item.ItemId, 0) with
            {
                Attribute = grant.Reward.Ability, Durability = grant.Item.Durability,
                SealFlag = placement.WorldClass == ItemWorldClass.Creature ? (byte)0 : grant.Item.Sealed ? (byte)1 : (byte)0,
                ExpireTime = placement.WorldClass == ItemWorldClass.Avatar ? 0 : grant.Expiration,
            };
            var remaining = grant.Reward.Count;
            while (remaining > 0)
            {
                var candidates = CandidateSlots(placement).ToArray();
                var piles = candidates.Where(slot => standing[slot.Space].TryGetValue(slot.Index, out var item)
                        && item.IsPile && item.ItemId == grant.Item.ItemId
                        && item.Core with { Value = 0 } == baseline)
                    .Select(slot => new ItemPile(slot, standing[slot.Space][slot.Index].Count)).ToArray();
                var arrival = arrivals.ForGrant(piles, remaining, placement,
                    candidates.Where(slot => standing[slot.Space].ContainsKey(slot.Index)).ToHashSet());
                BusinessSlot slot; int added; InventoryItemCoreState core; InventoryItemCreationFacts? facts = null;
                if (arrival is ItemArrival.OntoExistingPile pile)
                { slot = pile.Slot; added = pile.Added; core = standing[slot.Space][slot.Index].Core with { Value = pile.Total }; }
                else if (arrival is ItemArrival.IntoEmptySlot empty)
                {
                    slot = empty.Slot; added = empty.Count;
                    core = baseline with { Value = placement.WorldClass == ItemWorldClass.Interchangeable ? added : (int)(identity++ % (int.MaxValue - 1)) + 1 };
                    facts = placement.WorldClass switch
                    {
                        ItemWorldClass.Avatar => new(new(grant.Expiration, 0, new byte[30], 0, 0, 0)),
                        ItemWorldClass.Creature => new(Creature: new(100, 0, 0, 0, 0, grant.Item.CreatureStartLevel!.Value, [], 0)),
                        _ => null,
                    };
                }
                else throw new OpeningRejectedException(OpeningRejection.NoRoom, $"No room for {grant.Item.ItemId} in {space}.");
                if (added <= 0 || added > remaining) throw new InvalidOperationException("Nonprogressing grant.");
                remaining -= added;
                standing[slot.Space][slot.Index] = new(slot.Index, core, placement.WorldClass);
                writes[slot] = (core, facts);
                displayed.Add(new(slot, grant.Reward.ItemId, added,
                    core.Durability, core.Attribute, grant.Expiration));
            }
        }
        foreach (var (slot, write) in writes.OrderBy(p => p.Key.Space).ThenBy(p => p.Key.Index))
        {
            var bag = bags[slot.Space];
            if (write.Core is null) await bag.ClearAsync(slot.Index, ct).ConfigureAwait(false);
            else if (write.Facts is { } facts) await bag.PutNewAsync(slot.Index, write.Core, facts, ct).ConfigureAwait(false);
            else await bag.PutAsync(slot.Index, write.Core, ct).ConfigureAwait(false);
        }
        foreach (var (index, core) in virtualWrites)
            await mainStorage.PutAsync(index, core, ct).ConfigureAwait(false);
        await store.SessionIn(transaction).ApplyDecidedWritesAsync(ct).ConfigureAwait(false);
        if (useRule.CooldownMilliseconds > 0)
            transaction.Enlist(new object(), () => new OpeningCooldown(sessions, cooldowns, session,
                useRules.CooldownKeyFor(consumed[0].ItemId, useRule), TimeSpan.FromMilliseconds(useRule.CooldownMilliseconds)));
        var snapshot = await inventory.LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var updates = ImmutableArray.CreateBuilder<OpeningInventoryUpdate>();
        foreach (var slot in writes.Where(w => w.Value.Core is not null).Select(w => w.Key)
            .Concat(virtualWrites.Keys.Select(index => new BusinessSlot(BusinessInventorySpace.VirtualResource, index))))
        {
            var storage = slot.Space == BusinessInventorySpace.VirtualResource
                ? new LegacyInventoryStorageCoordinate(InventoryContainerType.Main, slot.Index)
                : LegacyInventoryStorageCoordinates.ToStorage(slot);
            var item = snapshot.Containers.Single(b => b.ContainerType == storage.List).Items
                .SingleOrDefault(i => i.SlotIndex == storage.Index)
                ?? throw new InvalidDataException("Committed opening reward missing from projection.");
            updates.Add(new(slot, item.Identity, item.Core));
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
        return new(session, updates.ToImmutable(), writes.Where(w => w.Value.Core is null)
            .Select(w => w.Key).ToImmutableArray(), displayed.ToImmutable(), now,
            changedPremiums.Select(p => new AccountPremiumEntryState(p.Key, p.Value)).ToImmutableArray(), pets, creaturesChanged);
    }

    private static IEnumerable<BusinessSlot> CandidateSlots(ItemPlacement placement)
    {
        var candidates = Enumerable.Range(
                placement.Destination.FirstIndex,
                placement.Destination.LastIndex - placement.Destination.FirstIndex + 1)
            .Select(index => new BusinessSlot(placement.Destination.Space, index));
        if (ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind))
            candidates = candidates.Concat(Enumerable.Range(0, 6)
                .Select(index => new BusinessSlot(BusinessInventorySpace.QuickSlot, index)));
        return candidates;
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
