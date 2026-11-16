using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Inventory;

internal sealed partial class SqliteItemDomainStoreSession : IAuctionItemCustodySession
{
    public ValueTask AcceptAuctionLotAsync(AuctionListingId listing, ItemRecord record, CancellationToken ct) =>
        AcceptCustodyAsync(new AuctionCustodyStorage(owner, listing), record, ct);

    public ValueTask<ItemRecord> TakeAuctionLotAsync(AuctionListingId listing, int? count, CancellationToken ct) =>
        TakeCustodyAsync(new AuctionCustodyStorage(owner, listing), count, ct);

    private sealed class AuctionCustodyStorage(SqliteDatabaseParticipant participant, AuctionListingId listing)
        : CustodyStorage(participant)
    {
        protected override string Table => "auction_custody";
        protected override string Key => "listing_id=@listing";
        protected override string InsertColumns => "listing_id";
        protected override string InsertValues => "@listing";
        protected override void Bind(SqliteCommand command) => command.Parameters.AddWithValue("@listing", listing.Value);
    }
}
