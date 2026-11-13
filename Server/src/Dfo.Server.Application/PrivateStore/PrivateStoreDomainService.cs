using Dfo.Server.Data.Accounts;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Mail;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.PrivateStore;

/// <summary>The one owner of open stalls and the goods laid out on them, behind the write and the read face.</summary>
/// <remarks>
/// A stall lives in memory for as long as its seller's selected session is in the world; its goods
/// are held in <c>private_store_custody</c> in the same transaction that took them, so a stall the
/// process forgets (a disconnect, a restart) still has every item, and the return task mails them back.
/// Rules are 70's as the user decided for #43: the trade licence or the auction coupon opens a 14-line
/// stall where the seller stands; a shop doll opens one of its own size, consumed when goods are laid
/// out, whose takings and leftovers go by mail. A sale pays the seller at once (a doll's by mail) less
/// the A21 licence's 1% fee.
/// </remarks>
internal sealed class PrivateStoreDomainService(
    IPrivateStoreRuleService rules,
    IAuctionRuleService itemRules,
    IPremiumServiceDomainService premiums,
    ISelectedSessionDomainService sessions,
    ITownAreaDomainService townAreas,
    ICharacterBusinessInventoryDomainService inventories,
    ICharacterDomainService characters,
    IItemLifecycleDomainService lifecycle,
    IItemDomainStore items,
    IPrivateStoreDomainStore store,
    IMailboxDomainService mailbox,
    MailItemTransfers transfers,
    IDomainTransactionAccessor transactions,
    TimeProvider clock) : IPrivateStoreDomainService, IPrivateStoreQueryDomainService
{
    private const string LetterTitle = "个人商店";

    private sealed class Stall(SelectedSessionIdentity owner, PrivateStoreSpot spot, BusinessSlot? doll, int? dollItemId)
    {
        public SelectedSessionIdentity Owner { get; } = owner;
        public PrivateStoreSpot Spot { get; } = spot;
        public BusinessSlot? DollSlot { get; } = doll;
        public int? DollItemId { get; } = dollItemId;
        // Only what the stall is; its goods are custody's (private_store_custody), read when shown or bought.
        public byte[]? Title { get; set; }
        public PrivateStoreView View() => new(Owner, Spot, DollSlot is not null, Title);
    }

    private readonly Lock gate = new();
    private readonly Dictionary<CharacterId, Stall> stalls = [];
    // Keyed by character for lookup, but every visit holds the exact visitor and owner sessions.
    private readonly Dictionary<CharacterId, (SelectedSessionIdentity Visitor, SelectedSessionIdentity Owner)> visitors = [];

    public async ValueTask<PrivateStoreView> OpenAsync(SelectedSessionIdentity owner, PrivateStoreSpot spot, BusinessSlot? doll, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(PrivateStoreDomainService) + "." + nameof(OpenAsync));
        if (!sessions.IsInWorld(owner)) throw Refuse(PrivateStoreRefusal.NotHere);
        if (townAreas.Find(owner.CharacterId) is not { } standing || standing.Area.TownId != spot.TownId
            || standing.Area.AreaId != spot.AreaId)
            throw Refuse(PrivateStoreRefusal.NotHere);
        spot = spot with { ChannelId = standing.Area.ChannelId };
        int? dollItem = null;
        if (doll is { } dollSlot)
        {
            var held = await CarriedAsync(owner.CharacterId, dollSlot, ct).ConfigureAwait(false);
            if (held.Item is null || rules.DollCapacity(held.Item.ItemId) is null) throw Refuse(PrivateStoreRefusal.NotPermitted);
            dollItem = held.Item.ItemId;
        }
        else if (!await IsPermittedAsync(owner.AccountId, ct).ConfigureAwait(false))
            throw Refuse(PrivateStoreRefusal.NotPermitted);
        lock (gate)
        {
            if (stalls.TryGetValue(owner.CharacterId, out var existing) && sessions.IsInWorld(existing.Owner))
                throw Refuse(PrivateStoreRefusal.AlreadyOpen);
            var stall = new Stall(owner, spot, doll, dollItem);
            stalls[owner.CharacterId] = stall;
            return stall.View();
        }
    }

    public async ValueTask<(PrivateStoreView Store, IReadOnlyList<MailAssetChange> Changes)> DisplayAsync(SelectedSessionIdentity owner,
        byte[] title, IReadOnlyList<PrivateStoreDisplayLine> lines, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(PrivateStoreDomainService) + "." + nameof(DisplayAsync));
        RequireWritable();
        var stall = Current(owner) ?? throw Refuse(PrivateStoreRefusal.NotOpen);
        if (stall.Title is not null) throw Refuse(PrivateStoreRefusal.NotOpen);
        if (lines.Count == 0 || lines.Count > rules.Capacity(stall.DollItemId)) throw Refuse(PrivateStoreRefusal.TooManyItems);
        if (lines.Select(line => line.DisplaySlot).Distinct().Count() != lines.Count || lines.Any(line => line.DisplaySlot > 13)
            || lines.Select(line => line.Source).Distinct().Count() != lines.Count)
            throw Refuse(PrivateStoreRefusal.InvalidPrice);
        var ordinary = itemRules.EntitlementFor([]);
        var custody = Custody();
        var changes = new List<MailAssetChange>();
        long total = 0;
        foreach (var line in lines.OrderBy(line => line.DisplaySlot))
        {
            if (line.UnitPrice is <= 0 or > int.MaxValue) throw Refuse(PrivateStoreRefusal.InvalidPrice);
            var held = await CarriedAsync(owner.CharacterId, line.Source, ct).ConfigureAwait(false);
            if (held.Item is not { } item) throw Refuse(PrivateStoreRefusal.ItemMoved);
            // The client leaves the count of a single item unset: equipment is always one.
            var count = item.IsPile ? line.Count : 1;
            if (count <= 0 || count > item.Count) throw Refuse(PrivateStoreRefusal.ItemMoved);
            if (await itemRules.CheckEligibilityAsync(item.Snapshot, ordinary, ct).ConfigureAwait(false) != AuctionRefusal.None)
                throw Refuse(PrivateStoreRefusal.NotEligible);
            total = checked(total + line.UnitPrice * count);
            if (total > int.MaxValue) throw Refuse(PrivateStoreRefusal.InvalidPrice);
            var record = await held.Container!.TakeAsync(item, count, ct).ConfigureAwait(false);
            await custody.AcceptPrivateStoreLineAsync(owner.CharacterId, line.DisplaySlot, line.UnitPrice, record, ct).ConfigureAwait(false);
            changes.Add(new MailAssetChange(owner.CharacterId, line.Source, item.ItemId, -count, null));
        }
        if (stall.DollSlot is { } dollSlot)
        {
            // 70 CompleteDisplay uses up the doll when the goods go out.
            var doll = await CarriedAsync(owner.CharacterId, dollSlot, ct).ConfigureAwait(false);
            if (doll.Item is not { } dollItem || dollItem.ItemId != stall.DollItemId) throw Refuse(PrivateStoreRefusal.ItemMoved);
            await lifecycle.DeleteItemAsync(await doll.Container!.TakeAsync(dollItem, 1, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            changes.Add(new MailAssetChange(owner.CharacterId, dollSlot, dollItem.ItemId, -1, null));
        }
        lock (gate)
        {
            stall.Title = title;
            return (stall.View(), changes);
        }
    }

    public async ValueTask<PrivateStoreClosure?> CloseAsync(SelectedSessionIdentity owner, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(PrivateStoreDomainService) + "." + nameof(CloseAsync));
        RequireWritable();
        Stall? stall;
        lock (gate)
        {
            if (!stalls.TryGetValue(owner.CharacterId, out stall) || stall.Owner != owner) return null;
        }
        var custody = Custody();
        var changes = new List<MailAssetChange>();
        var mailed = false;
        foreach (var line in await custody.ReadPrivateStoreLinesAsync(owner.CharacterId, ct).ConfigureAwait(false))
        {
            var record = await custody.TakePrivateStoreLineAsync(owner.CharacterId, line.DisplaySlot, null, ct).ConfigureAwait(false);
            if (stall.DollSlot is null && await TryPutAsync(owner.CharacterId, record, changes, ct).ConfigureAwait(false)) continue;
            await MailItemAsync(owner.CharacterId, record, "个人商店已关闭，未售出的物品已退回。", ct).ConfigureAwait(false);
            mailed = true;
        }
        lock (gate)
        {
            stalls.Remove(owner.CharacterId);
            ForgetVisitsTo(owner.CharacterId);
        }
        return new(stall.View(), changes, mailed);
    }

    public async ValueTask<PrivateStorePurchase> BuyAsync(SelectedSessionIdentity buyer, CharacterId seller, byte displaySlot, int count,
        CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(PrivateStoreDomainService) + "." + nameof(BuyAsync));
        RequireWritable();
        Stall stall;
        lock (gate)
        {
            if (!stalls.TryGetValue(seller, out var found) || !sessions.IsInWorld(found.Owner) || found.Title is null)
                throw Refuse(PrivateStoreRefusal.NoSuchStore);
            stall = found;
        }
        var custody = Custody();
        var line = (await custody.ReadPrivateStoreLinesAsync(seller, ct).ConfigureAwait(false))
            .FirstOrDefault(value => value.DisplaySlot == displaySlot) ?? throw Refuse(PrivateStoreRefusal.NoSuchItem);
        if (stall.Owner.AccountId == buyer.AccountId) throw Refuse(PrivateStoreRefusal.OwnStore);
        var amount = line.Snapshot.IsPile ? count : 1;
        if (amount <= 0 || amount > line.Snapshot.Count) throw Refuse(PrivateStoreRefusal.NoSuchItem);
        var price = line.UnitPrice * amount;
        if (price > int.MaxValue) throw Refuse(PrivateStoreRefusal.InvalidPrice);
        var fee = rules.Fee(price);

        var buyerGold = await characters.Get(buyer.CharacterId).TrySpendGoldAsync((int)price, ct).ConfigureAwait(false)
            ?? throw Refuse(PrivateStoreRefusal.InsufficientFunds);
        int? sellerGold = null;
        if (stall.DollSlot is not null)
            await MailGoldAsync(seller, checked((int)(price - fee)), $"个人商店售出物品，所得 {price - fee:N0} 金币（手续费 {fee:N0}）。", ct).ConfigureAwait(false);
        else if (price - fee > 0)
        {
            var account = characters.Get(seller);
            var before = await account.GoldAsync(ct).ConfigureAwait(false);
            sellerGold = await account.AddGoldAsync((int)(price - fee), ct).ConfigureAwait(false);
            if ((long)sellerGold - before != price - fee) throw Refuse(PrivateStoreRefusal.SellerGoldLimit);
        }
        var record = await custody.TakePrivateStoreLineAsync(seller, displaySlot, amount, ct).ConfigureAwait(false);
        var changes = new List<MailAssetChange> { new(buyer.CharacterId, null, 0, -(int)price, buyerGold) };
        if (!await TryPutAsync(buyer.CharacterId, record, changes, ct).ConfigureAwait(false)) throw Refuse(PrivateStoreRefusal.NoRoom);
        return new(stall.View(), displaySlot, amount, line.Snapshot.Count - amount, price, fee, buyerGold, sellerGold, record.Snapshot, changes);
    }

    public async ValueTask<IReadOnlyList<PrivateStoreLine>> ReadGoodsAsync(CharacterId owner, CancellationToken ct)
    {
        if (Find(owner) is not { Displayed: true }) return [];
        return await Custody().ReadPrivateStoreLinesAsync(owner, ct).ConfigureAwait(false);
    }

    public async ValueTask<int> ReturnAbandonedAsync(int limit, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(PrivateStoreDomainService) + "." + nameof(ReturnAbandonedAsync));
        RequireWritable();
        var returned = 0;
        foreach (var owner in await store.OwnersWithCustodyAsync(ct).ConfigureAwait(false))
        {
            if (returned >= limit) break;
            lock (gate)
            {
                if (stalls.TryGetValue(owner, out var stall) && sessions.IsInWorld(stall.Owner)) continue;
                stalls.Remove(owner);
                ForgetVisitsTo(owner);
            }
            var custody = Custody();
            foreach (var line in await custody.ReadPrivateStoreLinesAsync(owner, ct).ConfigureAwait(false))
            {
                var record = await custody.TakePrivateStoreLineAsync(owner, line.DisplaySlot, null, ct).ConfigureAwait(false);
                await MailItemAsync(owner, record, "个人商店已关闭，未售出的物品已退回。", ct).ConfigureAwait(false);
            }
            returned++;
        }
        return returned;
    }

    // ---- Read face -----------------------------------------------------------------------------

    /// <remarks>Reads which premium services are active; the rule service says whether they permit a stall.</remarks>
    public async ValueTask<bool> IsPermittedAsync(AccountId account, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var active = (await premiums.For(account).ReadAsync(ct).ConfigureAwait(false))
            .Where(entry => entry.ExpiresAtUnixSeconds > now)
            .Select(entry => (PremiumServiceType)entry.PremiumType).ToArray();
        return rules.Permits(active);
    }

    public PrivateStoreView? Find(CharacterId owner)
    {
        lock (gate) return stalls.TryGetValue(owner, out var stall) && sessions.IsInWorld(stall.Owner) ? stall.View() : null;
    }

    public IReadOnlyList<PrivateStoreView> In(int channelId, byte townId, byte areaId)
    {
        lock (gate)
            return stalls.Values.Where(stall => stall.Title is not null && sessions.IsInWorld(stall.Owner)
                    && stall.Spot.ChannelId == channelId && stall.Spot.TownId == townId && stall.Spot.AreaId == areaId)
                .Select(stall => stall.View()).ToArray();
    }

    public void Enter(SelectedSessionIdentity visitor, SelectedSessionIdentity owner)
    {
        GameplayWriteAssertion.Writing(nameof(PrivateStoreDomainService) + "." + nameof(Enter));
        lock (gate)
        {
            // Visits whose sessions have ended are dropped here, the next time anyone walks in.
            foreach (var stale in visitors.Where(pair => !Live(pair.Value)).Select(pair => pair.Key).ToArray())
                visitors.Remove(stale);
            visitors[visitor.CharacterId] = (visitor, owner);
        }
    }

    public void Leave(SelectedSessionIdentity visitor)
    {
        GameplayWriteAssertion.Writing(nameof(PrivateStoreDomainService) + "." + nameof(Leave));
        lock (gate)
            if (visitors.TryGetValue(visitor.CharacterId, out var visit) && visit.Visitor == visitor)
                visitors.Remove(visitor.CharacterId);
    }

    public IReadOnlyList<SelectedSessionIdentity> VisitorsOf(SelectedSessionIdentity owner)
    {
        lock (gate)
            return visitors.Values.Where(visit => visit.Owner == owner && Live(visit)).Select(visit => visit.Visitor).ToArray();
    }

    public SelectedSessionIdentity? Visiting(SelectedSessionIdentity visitor)
    {
        lock (gate)
            return visitors.TryGetValue(visitor.CharacterId, out var visit) && visit.Visitor == visitor && Live(visit) ? visit.Owner : null;
    }

    /// <summary>Both sessions are still in the world and the owner session still keeps its stall.</summary>
    private bool Live((SelectedSessionIdentity Visitor, SelectedSessionIdentity Owner) visit) =>
        sessions.IsInWorld(visit.Visitor) && sessions.IsInWorld(visit.Owner)
        && stalls.TryGetValue(visit.Owner.CharacterId, out var stall) && stall.Owner == visit.Owner;

    private void ForgetVisitsTo(CharacterId owner)
    {
        foreach (var visitor in visitors.Where(pair => pair.Value.Owner.CharacterId == owner).Select(pair => pair.Key).ToArray())
            visitors.Remove(visitor);
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private Stall? Current(SelectedSessionIdentity owner)
    {
        lock (gate) return stalls.TryGetValue(owner.CharacterId, out var stall) && stall.Owner == owner ? stall : null;
    }


    private async ValueTask<(IBusinessItemContainer? Container, ItemReference? Item)> CarriedAsync(CharacterId owner, BusinessSlot slot,
        CancellationToken ct)
    {
        if (!slot.Space.IsCarriedItemSpace() || slot.Index < 0) return (null, null);
        var container = inventories.GetPersistentStore(new ContainerOwner(InventoryOwnerType.Character, owner.Value),
            BusinessInventoryOwnership.StoreOf(slot.Space)).FindContainer(slot.Space);
        return container is null ? (null, null) : (container, await container.GetAsync(slot.Index, ct).ConfigureAwait(false));
    }

    private async ValueTask<bool> TryPutAsync(CharacterId character, ItemRecord record, List<MailAssetChange> changes, CancellationToken ct)
    {
        try
        {
            changes.AddRange(await transfers.GrantAsync(character, [record], ct).ConfigureAwait(false));
            return true;
        }
        catch (MailException full) when (full.Reason == MailFailure.NoCapacity)
        {
            return false;
        }
    }

    private async ValueTask MailItemAsync(CharacterId recipient, ItemRecord record, string body, CancellationToken ct)
    {
        var builder = MailBuilder.FromSystem(recipient).WithText(LetterTitle, body);
        MailAsset asset = record.IsPile
            ? new MailAsset.Stackable(record.ItemId, record.Count)
            : new MailAsset.Equipment(record.ItemUid ?? throw new InvalidDataException("Held equipment has no identity."));
        if (record.IsPile) builder.AddStackable(record.ItemId, record.Count); else builder.AddEquipment(record.ItemUid!.Value);
        var mail = builder.Build();
        _ = await mailbox.SendPreparedAsync(mail, [new PreparedMailAttachment(mail.Attachments[0], [record])], ct).ConfigureAwait(false);
    }

    private async ValueTask MailGoldAsync(CharacterId recipient, int gold, string body, CancellationToken ct)
    {
        if (gold <= 0) return;
        var mail = MailBuilder.FromSystem(recipient).WithText(LetterTitle, body).AddGold(gold).Build();
        _ = await mailbox.SendPreparedAsync(mail, [new PreparedMailAttachment(mail.Attachments[0])], ct).ConfigureAwait(false);
    }

    private IPrivateStoreItemCustodySession Custody() =>
        items.SessionIn(transactions.Current ?? throw new InvalidOperationException("Stall custody requires the item transaction."))
            as IPrivateStoreItemCustodySession ?? throw new InvalidOperationException("This item store has no stall custody.");

    private void RequireWritable()
    {
        if (transactions.Current is not { IsReadOnly: false })
            throw new InvalidOperationException("Private store changes require a writable transaction.");
    }

    private static PrivateStoreRefusedException Refuse(PrivateStoreRefusal refusal) => new(refusal);
}
