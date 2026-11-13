using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Persistence.Inventory;

/// <summary>Auction custody: the one record a listing holds, kept in the item transaction like a mail attachment.</summary>
/// <remarks>
/// Equipment keeps its <c>items</c> row and identity, with no position; a pile keeps its exact 82-byte
/// core in the custody row and its item row goes. Only items without avatar or creature details are
/// taken into custody: those details live beside the item row and are not carried here yet.
/// </remarks>
internal sealed partial class SqliteItemDomainStoreSession : IAuctionItemCustodySession
{
    public async ValueTask AcceptAuctionLotAsync(AuctionListingId listing, ItemRecord record, CancellationToken ct)
    {
        Active();
        ArgumentNullException.ThrowIfNull(record);
        var foreign = scope.IsForeign(record, identity);
        var uid = foreign ? 0 : Detached(record);
        var snapshot = record.Snapshot;
        Validate(snapshot);
        if (snapshot.AvatarDetail is not null || snapshot.CreatureDetail is not null || snapshot.IsMirror)
            throw new InvalidOperationException("Auction custody holds only items without instance details.");
        if (!snapshot.IsPile && (foreign || snapshot.ItemUid?.Value != uid))
            throw new InvalidOperationException("Equipment enters auction custody only as the detached record of its own row.");
        if (!foreign && await HasPositionAsync(uid, ct).ConfigureAwait(false))
            throw new InvalidOperationException("A detached item still occupies an inventory position.");
        await Mutate(async () =>
        {
            await using (var command = await owner.CommandAsync(ct).ConfigureAwait(false))
            {
                command.CommandText = """
                    INSERT INTO auction_custody(listing_id,kind,item_uid,item_id,item_count,item_core)
                    VALUES(@listing,@kind,@uid,@item,@count,@core)
                    """;
                command.Parameters.AddWithValue("@listing", listing.Value);
                command.Parameters.AddWithValue("@kind", snapshot.IsPile ? "stackable" : "equipment");
                command.Parameters.AddWithValue("@uid", snapshot.IsPile ? 0 : uid);
                command.Parameters.AddWithValue("@item", snapshot.ItemId);
                command.Parameters.AddWithValue("@count", snapshot.Count);
                command.Parameters.AddWithValue("@core", snapshot.IsPile ? InventoryItemCore.Encode(snapshot.ToCore()) : DBNull.Value);
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            if (snapshot.IsPile && uid != 0) await RemoveRowAsync(uid, ct).ConfigureAwait(false);
            await scope.AcceptAsync(record, identity, ct).ConfigureAwait(false);
            detached.Remove(record);
            return true;
        }).ConfigureAwait(false);
    }

    public async ValueTask<ItemRecord> TakeAuctionLotAsync(AuctionListingId listing, int? count, CancellationToken ct)
    {
        Active();
        string kind; long uid; int itemId; int held; byte[]? core;
        await using (var command = await owner.CommandAsync(ct).ConfigureAwait(false))
        {
            command.CommandText = "SELECT kind,item_uid,item_id,item_count,item_core FROM auction_custody WHERE listing_id=@listing";
            command.Parameters.AddWithValue("@listing", listing.Value);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("The listing holds nothing in custody.");
            kind = reader.GetString(0); uid = reader.GetInt64(1); itemId = reader.GetInt32(2); held = reader.GetInt32(3);
            core = reader.IsDBNull(4) ? null : (byte[])reader.GetValue(4);
        }
        var amount = count ?? held;
        if (amount <= 0 || amount > held || kind == "equipment" && amount != 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (kind == "equipment")
        {
            if (await HasPositionAsync(uid, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Equipment in auction custody also occupies a position.");
            var snapshot = await ReadMailWorldAsync(checked((int)uid), ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Equipment in auction custody has no item row.");
            if (snapshot.ItemId != itemId || snapshot.IsPile)
                throw new InvalidDataException("Auction custody disagrees with its equipment row.");
            return await Mutate(async () =>
            {
                await DeleteCustodyAsync(listing, ct).ConfigureAwait(false);
                return Issue(uid, snapshot);
            }).ConfigureAwait(false);
        }
        if (core is not { Length: InventoryItemCore.Size })
            throw new InvalidDataException("Auction pile custody has no core.");
        var pile = ItemSnapshot.FromCore(InventoryItemCore.Decode(core), true);
        if (pile.ItemId != itemId) throw new InvalidDataException("Auction custody disagrees with its pile core.");
        Validate(pile with { Count = held });
        return await Mutate(async () =>
        {
            if (amount == held) await DeleteCustodyAsync(listing, ct).ConfigureAwait(false);
            else
            {
                await using var update = await owner.CommandAsync(ct).ConfigureAwait(false);
                update.CommandText = "UPDATE auction_custody SET item_count=@count WHERE listing_id=@listing AND item_count=@held";
                update.Parameters.AddWithValue("@count", held - amount);
                update.Parameters.AddWithValue("@held", held);
                update.Parameters.AddWithValue("@listing", listing.Value);
                if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("Auction custody changed during a take.");
            }
            var piece = pile with { Count = amount };
            return Issue(await InsertAsync(piece, ct).ConfigureAwait(false), piece);
        }).ConfigureAwait(false);
    }

    private async ValueTask DeleteCustodyAsync(AuctionListingId listing, CancellationToken ct)
    {
        await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
        command.CommandText = "DELETE FROM auction_custody WHERE listing_id=@listing";
        command.Parameters.AddWithValue("@listing", listing.Value);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Auction custody disappeared.");
    }
}
