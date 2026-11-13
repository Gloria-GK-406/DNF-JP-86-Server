using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.PrivateStore;

/// <summary>Why the private store refused. Facts only; the protocol maps them to codes.</summary>
public enum PrivateStoreRefusal
{
    None,
    NotPermitted,
    NotHere,
    AlreadyOpen,
    NotOpen,
    NotDisplayed,
    ItemMoved,
    NotEligible,
    TooManyItems,
    InvalidPrice,
    NoSuchStore,
    NoSuchItem,
    OwnStore,
    InsufficientFunds,
    SellerGoldLimit,
    NoRoom,
}

/// <summary>Raised inside a private store write so the caller's unit rolls everything back.</summary>
public sealed class PrivateStoreRefusedException(PrivateStoreRefusal refusal)
    : InvalidOperationException($"Private store refused: {refusal}.")
{
    public PrivateStoreRefusal Refusal { get; } = refusal;
}

/// <summary>Where a stall stands.</summary>
public sealed record PrivateStoreSpot(int ChannelId, byte TownId, byte AreaId, short X, short Y);

/// <summary>One line the seller lays out: what, from where, at what unit price.</summary>
public sealed record PrivateStoreDisplayLine(byte DisplaySlot, long UnitPrice, BusinessSlot Source, int Count);

/// <summary>One line on sale.</summary>
/// <param name="Snapshot">The item as held in custody; for a pile its count is what is left.</param>
public sealed record PrivateStoreLine(byte DisplaySlot, long UnitPrice, ItemSnapshot Snapshot);

/// <summary>An open stall as anyone may see it.</summary>
/// <param name="Doll">The stall is kept by a shop doll: the seller may walk away and is paid by mail.</param>
public sealed record PrivateStoreView(
    SelectedSessionIdentity Owner,
    PrivateStoreSpot Spot,
    bool Doll,
    byte[]? Title,
    IReadOnlyList<PrivateStoreLine> Lines)
{
    public bool Displayed => Title is not null;
}

/// <summary>What a purchase did, for the buyer's reply and the others' updates.</summary>
public sealed record PrivateStorePurchase(
    PrivateStoreView Store,
    byte DisplaySlot,
    int Count,
    int RemainingCount,
    long Price,
    long Fee,
    int BuyerGold,
    int? SellerGold,
    ItemSnapshot Bought,
    IReadOnlyList<MailAssetChange> BuyerChanges);

/// <summary>What closing a stall did: what went back to the bag, what had to go by mail.</summary>
public sealed record PrivateStoreClosure(PrivateStoreView Store, IReadOnlyList<MailAssetChange> Changes, bool Mailed);

/// <summary>The private store: open stalls, their goods in custody, browsing and buying.</summary>
/// <remarks>
/// Protocol-free. Stalls live as long as the seller's selected session; their goods are held in
/// custody in the database, so a stall lost to a disconnect or a restart is mailed back to its
/// seller by <see cref="ReturnAbandonedAsync"/>. Refusals after something was taken are thrown as
/// <see cref="PrivateStoreRefusedException"/>.
/// </remarks>
public interface IPrivateStoreDomainService
{
    /// <summary>Puts a stall up at the spot; with <paramref name="doll"/> a shop doll keeps it.</summary>
    ValueTask<PrivateStoreView> OpenAsync(SelectedSessionIdentity owner, PrivateStoreSpot spot, BusinessSlot? doll, CancellationToken ct);

    /// <summary>Takes the lines' items into custody and names the stall.</summary>
    ValueTask<(PrivateStoreView Store, IReadOnlyList<MailAssetChange> Changes)> DisplayAsync(SelectedSessionIdentity owner, byte[] title,
        IReadOnlyList<PrivateStoreDisplayLine> lines, CancellationToken ct);

    /// <summary>Takes the stall down; unsold goods go back to the bag, or by mail when they do not fit.</summary>
    ValueTask<PrivateStoreClosure?> CloseAsync(SelectedSessionIdentity owner, CancellationToken ct);

    ValueTask<PrivateStorePurchase> BuyAsync(SelectedSessionIdentity buyer, CharacterId seller, byte displaySlot, int count, CancellationToken ct);

    /// <summary>Mails back goods of stalls whose seller is no longer there to keep them.</summary>
    ValueTask<int> ReturnAbandonedAsync(int limit, CancellationToken ct);
}

/// <summary>Reads stalls; a domain service so notification handlers may ask it.</summary>
public interface IPrivateStoreQueryDomainService
{
    PrivateStoreView? Find(CharacterId owner);

    /// <summary>The displayed stalls standing in the town area.</summary>
    IReadOnlyList<PrivateStoreView> In(int channelId, byte townId, byte areaId);

    /// <summary>Records that a visitor is looking at a stall, so it is told of sales and closing.</summary>
    void Enter(CharacterId visitor, CharacterId owner);

    void Leave(CharacterId visitor);

    /// <summary>The visitors looking at the owner's stall.</summary>
    IReadOnlyList<CharacterId> VisitorsOf(CharacterId owner);

    /// <summary>The stall the visitor is looking at.</summary>
    CharacterId? Visiting(CharacterId visitor);
}

/// <summary>Private store terms. The one place its PVF and configuration facts are read.</summary>
public interface IPrivateStoreRuleService
{
    /// <summary>How many lines a stall may show: 14 with the licence, the doll's own number otherwise.</summary>
    int Capacity(int? dollItemId);

    /// <summary>The shop doll an item is, as its slot count; null when it is no doll.</summary>
    int? DollCapacity(int itemId);

    long Fee(long amount);
}

/// <summary>Where stall goods and their custody are kept. Data operations only.</summary>
public interface IPrivateStoreDomainStore
{
    ValueTask<IReadOnlyList<CharacterId>> OwnersWithCustodyAsync(CancellationToken ct);
}

/// <summary>Stall custody inside the item transaction, beside mail and auction custody.</summary>
public interface IPrivateStoreItemCustodySession
{
    ValueTask AcceptPrivateStoreLineAsync(CharacterId owner, byte displaySlot, long unitPrice, ItemRecord record, CancellationToken ct);

    /// <summary>Takes <paramref name="count"/> units (all when null) of the line.</summary>
    ValueTask<ItemRecord> TakePrivateStoreLineAsync(CharacterId owner, byte displaySlot, int? count, CancellationToken ct);

    /// <summary>The owner's lines in custody, by display slot.</summary>
    ValueTask<IReadOnlyList<PrivateStoreLine>> ReadPrivateStoreLinesAsync(CharacterId owner, CancellationToken ct);
}
