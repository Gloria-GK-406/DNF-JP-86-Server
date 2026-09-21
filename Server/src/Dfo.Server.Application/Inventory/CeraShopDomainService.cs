using System.Collections.Immutable;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class CeraShopDomainService(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers, ICharacterDomainService characters,
    ICharacterTable characterRows, IAccountTable accounts, ICharacterContainerDomainService containers,
    ICeraShopRuleService rules, IItemDomainStore itemStore, IDomainTransactionAccessor transactions,
    ICharacterInventoryEntryDomainService inventory, ICharacterCreatureTable creatures, IPremiumServiceDomainService premiums) : ICeraShopDomainService
{
    public async ValueTask<CeraShopPurchaseResult> PurchaseAsync(SelectedSessionIdentity session, CeraShopPurchaseInput input,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var transaction = transactions.Current
            ?? throw new InvalidOperationException("CERA purchases require a transaction boundary.");
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId
            || !await characterRows.BelongsToAsync(session.CharacterId, session.AccountId, cancellationToken).ConfigureAwait(false))
            return CeraShopPurchaseResult.Refused(CeraShopRejection.NoSession);

        var character = characters.Get(session.CharacterId);
        var progress = await character.ProgressAsync(cancellationToken).ConfigureAwait(false);
        var wallet = await accounts.FindWalletAsync(session.AccountId, cancellationToken).ConfigureAwait(false);
        if (progress is null || wallet is null)
            return CeraShopPurchaseResult.Refused(CeraShopRejection.NoSession);
        var quoted = await rules.QuoteAsync(input, progress.Level, now, cancellationToken).ConfigureAwait(false);
        if (quoted.Quote is not { } quote)
            return CeraShopPurchaseResult.Refused(quoted.Rejection, quoted.Detail);

        var bags = new Dictionary<InventoryContainerType, IItemContainer>();
        var held = new Dictionary<InventoryContainerType, IReadOnlyList<ContainerSlotItem>>();
        foreach (var type in (InventoryContainerType[])[InventoryContainerType.Main, InventoryContainerType.Avatar, InventoryContainerType.Pet])
        {
            if (containers.FindContainer(session.CharacterId, type) is not { } bag) continue;
            bags.Add(type, bag);
            held.Add(type, await bag.ReadAllAsync(cancellationToken).ConfigureAwait(false));
        }
        var gold = await character.GoldAsync(cancellationToken).ConfigureAwait(false);
        var before = new AccountCeraBalances(checked((int)wallet.Cera), checked((int)wallet.TokenCera), checked((int)wallet.HappyTokenCera));
        var planned = rules.Plan(quote, held, gold, before, Random.Shared.Next(1, int.MaxValue));
        if (planned.Plan is not { } plan)
            return CeraShopPurchaseResult.Refused(planned.Rejection, planned.Detail);

        // From the first write onwards, every refusal must unwind the shared transaction.
        if (before != plan.Balances && await accounts.TrySetCeraBalancesAsync(session.AccountId, before, plan.Balances,
                cancellationToken).ConfigureAwait(false) != 1)
            throw new CeraShopPurchaseRejectedException(CeraShopRejection.StateChanged, "Account balances changed before settlement.");
        if (plan.GoldCost > 0)
            gold = await character.TrySpendGoldAsync(plan.GoldCost, cancellationToken).ConfigureAwait(false)
                ?? throw new CeraShopPurchaseRejectedException(CeraShopRejection.InsufficientFunds, "Gold changed before settlement.");
        foreach (var subscription in quote.Subscriptions)
            await premiums.For(session.AccountId).ExtendAsync(subscription.Service, subscription.Duration,
                cancellationToken).ConfigureAwait(false);
        foreach (var write in plan.Writes)
        {
            if (write.CreationFacts is { } facts)
                await bags[write.Container].PutNewAsync(write.SlotIndex, write.Core, facts, cancellationToken).ConfigureAwait(false);
            else await bags[write.Container].PutAsync(write.SlotIndex, write.Core, cancellationToken).ConfigureAwait(false);
        }
        await itemStore.SessionIn(transaction).ApplyDecidedWritesAsync(cancellationToken).ConfigureAwait(false);

        // Read through the entry projection: it validates ownership and carries avatar facts and
        // newly allocated world identities, which a pre-write plan cannot truthfully supply.
        var snapshot = await inventory.LoadAsync(session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        var updates = ImmutableArray.CreateBuilder<CommittedInventoryUpdateState>();
        foreach (var group in plan.Writes.GroupBy(write => write.Container).OrderBy(group => group.Key))
        {
            var changedSlots = group.Select(write => write.SlotIndex).ToHashSet();
            var items = snapshot.Containers.Single(container => container.ContainerType == group.Key).Items
                .Where(item => changedSlots.Contains(item.SlotIndex)).OrderBy(item => item.SlotIndex).ToArray();
            if (items.Length != changedSlots.Count)
                throw new InvalidDataException("A committed CERA grant is absent from its inventory projection.");
            updates.Add(new(group.Key, items, now));
        }
        var hasCreatures = plan.Writes.Any(write => write.CreationFacts?.Creature is not null);
        ImmutableArray<CharacterPetRuntimeState> pets = [];
        if (hasCreatures)
        {
            var rows = await creatures.ReadAsync(session.CharacterId, cancellationToken).ConfigureAwait(false);
            if (rows.Count > byte.MaxValue)
                throw new CeraShopPurchaseRejectedException(CeraShopRejection.NoRoom, "Creature list cannot fit its wire count.");
            pets = rows.OrderBy(row => row.SortOrder).Select(row => new CharacterPetRuntimeState(row.CreatureKey,
                row.Field04, checked((byte)row.ModeFlag), row.ProgressValue, row.Mode1Field0A, row.Mode1Field0B,
                row.FieldAfterValue, [.. row.CreatureText], checked((byte)row.TailFlag))).ToImmutableArray();
        }
        return new(CeraShopRejection.None, new(session, quote.ProductIds, plan.Balances, gold, plan.GoldCost > 0,
            updates.ToImmutable(), pets, hasCreatures, now), null);
    }
}
