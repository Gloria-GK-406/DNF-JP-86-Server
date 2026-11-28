using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Trade;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Trade;
using static Dfo.Server.Protocol.Game.Trade.TradeProjection;

namespace Dfo.Server.Protocol.Game.MoveItemSpace;

public sealed partial class GameMoveItemSpaceCommandHandler
{
    private const InventoryContainerType TradeSpace = (InventoryContainerType)4;
    private const InventoryContainerType CrystalSpace = (InventoryContainerType)36;

    private async ValueTask<TradeMoveResult?> MoveTradeAsync(CommandExecutionContext context, NetworkConnectionContext connection,
        GameMessageMetadata metadata, GameSlot source, GameSlot target, int itemId, int count, CancellationToken ct)
    {
        if (GameRequestFreshness.IsStale(metadata) || !selectedSessions.TryGetInWorld(connection.ConnectionId, out var actor)
            || trades.Find(actor) is not { Accepted: true } trade || !trades.IsUsable(trade)
            || source.Space == target.Space || count <= 0) return null;
        var withdraw = source.Space == TradeSpace;
        var bag = withdraw ? target : source;
        if (bag.Space is not (InventoryContainerType.Main or CrystalSpace)) return null;

        TradeOfferChange? change;
        GameSlot destination;
        if (withdraw)
        {
            change = trades.Withdraw(trade, actor, source.Index, itemId, count, target.Space == CrystalSpace);
            if (change is null) return null;
            destination = change.Source.Slot is { } address
                ? A21InventoryLayout.ToGameSlot(address) with { Space = target.Space }
                : new(target.Space, 0);
        }
        else
        {
            if (!TrySource(source, itemId, out var offer)) return null;
            // A live slot reference joins the transaction's writable container registry.
            // The quote reads it and leaves the asset in that slot.
            var quote = (await units.WriteAsync(context, new("trade-quote",
                Recipients(actor, trade.Opposite(actor), connection, metadata)),
                async (_, token) => new QuoteResult(await trades.PrepareOfferAsync(trade, actor, offer, count, token).ConfigureAwait(false)),
                ct).ConfigureAwait(false)).Offer;
            if (quote is null) return null;
            change = trades.ApplyOffer(trade, actor, quote);
            if (change is null) return null;
            destination = new(TradeSpace, change.TradeSlot);
        }
        return new(destination, trade.Opposite(actor), TradePackets.Item(change.TradeSlot,
            change.Source.ItemId, change.Count, change.Snapshot, time.GetUtcNow()));
    }

    private static bool TrySource(GameSlot source, int itemId, out TradeSource offer)
    {
        offer = new(null, itemId);
        if (source.Space == InventoryContainerType.Main && source.Index == 0) return itemId == 0;
        if (itemId <= 0 || source.Index is < 3 or > 359
            || source.Space == CrystalSpace && source.Index < 354) return false;
        if (source.Index >= 354 && !AccountMaterialCatalog.Definitions.Any(item =>
            item.VirtualSlotIndex == source.Index && item.ItemId == itemId)) return false;
        try
        {
            offer = new(A21InventoryLayout.ToBusinessSlot(source with { Space = InventoryContainerType.Main }), itemId);
            return true;
        }
        catch (InventorySlotMappingException) { return false; }
    }

    private sealed record QuoteResult(IPreparedTradeOffer? Offer);
    private sealed record TradeMoveResult(GameSlot Destination, SelectedSessionIdentity Opposite, TradeNotice Notice);
}
