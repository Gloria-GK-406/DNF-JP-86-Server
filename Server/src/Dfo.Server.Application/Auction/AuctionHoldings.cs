using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Application.Auction;

/// <summary>A lot as inspected before anything was taken.</summary>
internal sealed record InspectedLot(int ItemId, int Quantity, bool Pile, int GoldAmount, ItemSnapshot? Shown,
    BusinessSlot? Slot, IBusinessItemContainer? Container, ItemReference? Item);

/// <summary>What a lot is and how it is held and handed over: an item, or an amount of gold.</summary>
internal interface IAuctionLotHandling
{
    ValueTask<InspectedLot> InspectAsync(CharacterId seller, AuctionMarket market, AuctionLotSource source, bool hasCoupon, CancellationToken ct);

    ValueTask<IReadOnlyList<MailAssetChange>> TakeIntoCustodyAsync(CharacterId seller, AuctionListingId listing, InspectedLot lot, CancellationToken ct);

    /// <summary>Mails <paramref name="count"/> units of the lot (all when null), with <paramref name="gold"/> beside it.</summary>
    ValueTask DeliverAsync(AuctionListing listing, CharacterId recipient, int? count, int gold, AuctionLetter letter, CancellationToken ct);
}

/// <summary>The money side of a market: gold for items, Cera for gold consignment.</summary>
internal interface IAuctionPurse
{
    ValueTask<MailAssetChange> CollectGoldAsync(CharacterId character, int amount, CancellationToken ct);

    /// <summary>Takes a payment into the house; the change to show, or null when the currency is not carried gold.</summary>
    ValueTask<MailAssetChange?> CollectAsync(AuctionMarket market, CharacterId payer, AccountId account, long amount, CancellationToken ct);

    ValueTask RefundAsync(AuctionMarket market, CharacterId payer, AccountId account, long amount, AuctionLetter letter, CancellationToken ct);

    ValueTask PaySellerAsync(AuctionListing listing, long price, long fee, int deposit, int quantity, CancellationToken ct);
}

internal sealed class AuctionLotHandling(
    IAuctionRuleService rules,
    ICharacterBusinessInventoryDomainService inventories,
    ICharacterDomainService characters,
    IItemDomainStore items,
    IDomainTransactionAccessor transactions,
    IMailboxDomainService mailbox) : IAuctionLotHandling
{
    public async ValueTask<InspectedLot> InspectAsync(CharacterId seller, AuctionMarket market, AuctionLotSource source, bool hasCoupon,
        CancellationToken ct)
    {
        switch (source)
        {
            case AuctionLotSource.CarriedItem carried when market == AuctionMarket.Items:
                if (!carried.Slot.Space.IsCarriedItemSpace() || carried.Slot.Index < 0 || carried.Count <= 0)
                    throw new AuctionRefusedException(AuctionRefusal.ItemMoved);
                var container = inventories.GetPersistentStore(new ContainerOwner(InventoryOwnerType.Character, seller.Value),
                    BusinessInventoryOwnership.StoreOf(carried.Slot.Space)).FindContainer(carried.Slot.Space);
                var item = container is null ? null : await container.GetAsync(carried.Slot.Index, ct).ConfigureAwait(false);
                if (item is null || item.ItemId != carried.ItemId) throw new AuctionRefusedException(AuctionRefusal.ItemMoved);
                if (carried.Count > item.Count || !item.IsPile && carried.Count != 1)
                    throw new AuctionRefusedException(AuctionRefusal.ExceedsHolding);
                var eligibility = await rules.CheckEligibilityAsync(item.Snapshot, hasCoupon, ct).ConfigureAwait(false);
                if (eligibility != AuctionRefusal.None) throw new AuctionRefusedException(eligibility);
                var display = item.Snapshot with { Count = carried.Count };
                return new(item.ItemId, carried.Count, item.IsPile, 0, display, carried.Slot, container, item);
            case AuctionLotSource.Gold gold when market == AuctionMarket.GoldConsignment:
                var amount = rules.ConsignmentTierGold(gold.TierItemId) ?? throw new AuctionRefusedException(AuctionRefusal.NotEligible);
                if (await characters.Get(seller).GoldAsync(ct).ConfigureAwait(false) < amount)
                    throw new AuctionRefusedException(AuctionRefusal.InsufficientFunds);
                return new(gold.TierItemId, 1, false, amount, null, null, null, null);
            default:
                throw new AuctionRefusedException(AuctionRefusal.NotEligible);
        }
    }

    public async ValueTask<IReadOnlyList<MailAssetChange>> TakeIntoCustodyAsync(CharacterId seller, AuctionListingId listing,
        InspectedLot lot, CancellationToken ct)
    {
        if (lot.Container is { } container && lot.Item is { } item && lot.Slot is { } slot)
        {
            var record = await container.TakeAsync(item, lot.Quantity, ct).ConfigureAwait(false);
            await Custody().AcceptAuctionLotAsync(listing, record, ct).ConfigureAwait(false);
            return [new MailAssetChange(seller, slot, lot.ItemId, -lot.Quantity, null)];
        }
        var balance = await characters.Get(seller).TrySpendGoldAsync(lot.GoldAmount, ct).ConfigureAwait(false)
            ?? throw new AuctionRefusedException(AuctionRefusal.InsufficientFunds);
        return [new MailAssetChange(seller, null, 0, -lot.GoldAmount, balance)];
    }

    public async ValueTask DeliverAsync(AuctionListing listing, CharacterId recipient, int? count, int gold, AuctionLetter letter,
        CancellationToken ct)
    {
        var builder = MailBuilder.FromSystem(recipient).WithText(AuctionLetters.Title, letter.Body(listing));
        var prepared = new List<PreparedMailAttachment>();
        if (listing.Market == AuctionMarket.Items)
        {
            var record = await Custody().TakeAuctionLotAsync(listing.Id, count, ct).ConfigureAwait(false);
            MailAsset asset = record.IsPile
                ? new MailAsset.Stackable(record.ItemId, record.Count)
                : new MailAsset.Equipment(record.ItemUid ?? throw new InvalidDataException("Held equipment has no identity."));
            if (record.IsPile) builder.AddStackable(record.ItemId, record.Count); else builder.AddEquipment(record.ItemUid!.Value);
            prepared.Add(new(asset, [record]));
            var itemMail = builder.Build();
            _ = await mailbox.SendPreparedAsync(itemMail, Align(itemMail, prepared), ct).ConfigureAwait(false);
            // Gold beside an item goes in a letter of its own. The client lists every attachment as
            // its own row with a gold field, so an item and its deposit in one letter show a "0 gold"
            // item row next to the gold row; each claims separately anyway (user test, 2026-11-13).
            if (gold > 0) await SendGoldAsync(recipient, gold, AuctionLetters.DepositReturned(gold), ct).ConfigureAwait(false);
            return;
        }
        await SendGoldAsync(recipient, checked(gold + listing.GoldAmount), letter.Body(listing), ct).ConfigureAwait(false);
    }

    private async ValueTask SendGoldAsync(CharacterId recipient, int gold, string body, CancellationToken ct)
    {
        var mail = MailBuilder.FromSystem(recipient).WithText(AuctionLetters.Title, body).AddGold(gold).Build();
        _ = await mailbox.SendPreparedAsync(mail, [new PreparedMailAttachment(mail.Attachments[0])], ct).ConfigureAwait(false);
    }

    /// <summary>The prepared attachments in the mail's own attachment order, each against the asset value the mail holds.</summary>
    internal static IReadOnlyList<PreparedMailAttachment> Align(Dfo.Server.Data.Mail.Mail mail, IReadOnlyList<PreparedMailAttachment> prepared) =>
        mail.Attachments.Select((asset, index) => prepared[index] with { Asset = asset }).ToArray();

    private IAuctionItemCustodySession Custody() =>
        items.SessionIn(transactions.Current ?? throw new InvalidOperationException("Auction custody requires the item transaction."))
            as IAuctionItemCustodySession ?? throw new InvalidOperationException("This item store has no auction custody.");
}

internal sealed class AuctionPurse(
    ICharacterDomainService characters,
    IAccountWalletDomainService wallets,
    IMailboxDomainService mailbox) : IAuctionPurse
{
    public async ValueTask<MailAssetChange> CollectGoldAsync(CharacterId character, int amount, CancellationToken ct)
    {
        var balance = await characters.Get(character).TrySpendGoldAsync(amount, ct).ConfigureAwait(false)
            ?? throw new AuctionRefusedException(AuctionRefusal.InsufficientFunds);
        return new MailAssetChange(character, null, 0, -amount, balance);
    }

    public async ValueTask<MailAssetChange?> CollectAsync(AuctionMarket market, CharacterId payer, AccountId account, long amount,
        CancellationToken ct)
    {
        var value = checked((int)amount);
        if (market == AuctionMarket.Items) return await CollectGoldAsync(payer, value, ct).ConfigureAwait(false);
        var balance = await wallets.ReadAsync(account, ct).ConfigureAwait(false);
        if (balance is null || balance.Cera < value) throw new AuctionRefusedException(AuctionRefusal.InsufficientFunds);
        _ = await wallets.SpendAsync(account, AccountCurrency.Cera, value, ct).ConfigureAwait(false);
        return null;
    }

    public async ValueTask RefundAsync(AuctionMarket market, CharacterId payer, AccountId account, long amount, AuctionLetter letter,
        CancellationToken ct)
    {
        var value = checked((int)amount);
        if (market == AuctionMarket.GoldConsignment)
        {
            _ = await wallets.AddAsync(account, AccountCurrency.Cera, value, ct).ConfigureAwait(false);
            return;
        }
        await SendGoldAsync(payer, value, letter.Body(null, value), ct).ConfigureAwait(false);
    }

    public async ValueTask PaySellerAsync(AuctionListing listing, long price, long fee, int deposit, int quantity, CancellationToken ct)
    {
        var net = checked((int)(price - fee));
        if (listing.Market == AuctionMarket.GoldConsignment)
        {
            // As 70 makeSuccessfulBid does for its point auction: the seller is mailed a token Cera
            // voucher for the net amount, credited to the account when claimed (user decision 2026-11-13).
            var voucher = MailBuilder.FromSystem(listing.Seller)
                .WithText(AuctionLetters.Title, AuctionLetters.ConsignmentSold(listing, price, fee, net)).AddTokenCera(net).Build();
            _ = await mailbox.SendPreparedAsync(voucher, [new PreparedMailAttachment(voucher.Attachments[0])], ct).ConfigureAwait(false);
            return;
        }
        await SendGoldAsync(listing.Seller, checked(net + deposit),
            AuctionLetters.Sold(listing, quantity, price, fee, deposit), ct).ConfigureAwait(false);
    }

    private async ValueTask SendGoldAsync(CharacterId recipient, int amount, string body, CancellationToken ct)
    {
        var mail = MailBuilder.FromSystem(recipient).WithText(AuctionLetters.Title, body).AddGold(amount).Build();
        _ = await mailbox.SendPreparedAsync(mail, [new PreparedMailAttachment(mail.Attachments[0])], ct).ConfigureAwait(false);
    }
}
