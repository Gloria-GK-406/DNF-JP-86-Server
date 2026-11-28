using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Trade;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Trade;

/// <summary>Coordinates the existing peer and move commands with the dedicated trading state command.</summary>
public sealed class TradeProtocol(PlayerTradeDomainService trades, ISelectedSessionDomainService sessions,
    ICommandWorkUnits units, IGameCommandOutbox outbox, ICharacterInventoryEntryDomainService inventories,
    IAccountInventoryEntryDomainService accounts, TimeProvider clock)
{
    internal const byte PeerType = 1;
    internal const InventoryContainerType TradeSpace = (InventoryContainerType)4;
    internal const InventoryContainerType CrystalSpace = (InventoryContainerType)36;

    internal async ValueTask<bool> InviteAsync(CommandExecutionContext context, NetworkConnectionContext connection,
        GameMessageMetadata metadata, ushort target, int peer, byte[] tail, CancellationToken ct)
    {
        if (GameRequestFreshness.IsStale(metadata) || !tail.All(value => value == 0)
            || !sessions.TryGetInWorld(connection.ConnectionId, out var actor)) return false;
        if (!sessions.TryGetInWorld(target, out var other) || other == actor)
        {
            await SendAsync(actor, new TradePeerReply(0x000A, [0, 3, PeerType]), ct).ConfigureAwait(false);
            return false;
        }

        var allowed = await units.ReadOnlyAsync(context, new("trade-invitation",
            Recipients(actor, other, connection, metadata)),
            token => trades.CanInviteAsync(actor, other, token), ct).ConfigureAwait(false);
        if (!allowed || trades.Invite(actor, other, peer) is not { } trade)
        {
            await SendAsync(actor, new TradePeerReply(0x000A, [0, 19, PeerType]), ct).ConfigureAwait(false);
            return false;
        }

        await SendAsync(actor, new TradePeerReply(0x000A, [1]), ct).ConfigureAwait(false);
        await SendAsync(other, TradePackets.Invitation(actor.WireUserId, trade.Peer), ct).ConfigureAwait(false);
        return true;
    }

    internal async ValueTask<bool> RespondAsync(CommandExecutionContext context, NetworkConnectionContext connection,
        GameMessageMetadata metadata, ushort inviter, int peer, byte[] tail, CancellationToken ct)
    {
        if (GameRequestFreshness.IsStale(metadata) || !sessions.TryGetInWorld(connection.ConnectionId, out var actor)
            || trades.Find(actor) is not { Accepted: false } trade || trade.Invitee != actor
            || trade.Inviter.WireUserId != inviter) return false;
        var refused = tail.Length >= 2 && tail[0] == 0x85 && tail[1] == 0;
        if (!tail.Skip(refused ? 2 : 0).All(value => value == 0)) return false;
        if (refused || !trades.IsUsable(trade) || !await units.ReadOnlyAsync(context, new("trade-acceptance",
                Recipients(actor, trade.Inviter, connection, metadata)), token => trades.CanAcceptAsync(trade, token), ct)
                .ConfigureAwait(false) || !trades.Accept(trade))
        {
            await CloseAsync(trade, false, ct).ConfigureAwait(false);
            return false;
        }

        await SendAsync(actor, TradePackets.Accepted(inviter), ct).ConfigureAwait(false);
        await SendAsync(trade.Inviter, TradePackets.Open(actor.WireUserId, peer), ct).ConfigureAwait(false);
        return true;
    }

    internal async ValueTask<TradeMoveResult?> MoveAsync(CommandExecutionContext context, NetworkConnectionContext connection,
        GameMessageMetadata metadata, GameSlot source, GameSlot target, int itemId, int count, CancellationToken ct)
    {
        if (GameRequestFreshness.IsStale(metadata) || !sessions.TryGetInWorld(connection.ConnectionId, out var actor)
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
            change.Source.ItemId, change.Count, change.Snapshot, clock.GetUtcNow()));
    }

    internal async ValueTask<bool> AdvanceAsync(CommandExecutionContext context, NetworkConnectionContext connection,
        GameMessageMetadata metadata, byte state, CancellationToken ct)
    {
        if (GameRequestFreshness.IsStale(metadata) || !sessions.TryGetInWorld(connection.ConnectionId, out var actor)
            || trades.Find(actor) is not { } trade) return false;
        var action = state switch
        {
            5 => TradeAction.Register,
            1 => TradeAction.Ready,
            3 => TradeAction.Confirm,
            _ => TradeAction.Cancel,
        };
        var advance = trades.Advance(trade, actor, action);
        switch (advance)
        {
            case TradeAdvance.Rejected:
                return false;
            case TradeAdvance.Canceled:
                await CloseAsync(trade, false, ct).ConfigureAwait(false);
                return true;
            case TradeAdvance.Settle:
                var settled = await SettleAsync(context, trade, actor, connection, metadata, ct).ConfigureAwait(false);
                await CloseAsync(trade, settled, ct).ConfigureAwait(false);
                return settled;
        }

        if (action != TradeAction.Confirm)
            foreach (var member in trade.Participants)
                await SendAsync(member, TradePackets.State(actor.WireUserId, state), ct).ConfigureAwait(false);
        if (advance == TradeAdvance.BothRegistered)
            foreach (var member in trade.Participants)
                await SendAsync(member, TradePackets.Registered(), ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> SettleAsync(CommandExecutionContext context, PlayerTradeSession trade,
        SelectedSessionIdentity actor, NetworkConnectionContext connection, GameMessageMetadata metadata, CancellationToken ct)
    {
        try
        {
            return await units.WriteAsync(context, new("player-trade-settlement",
                Recipients(actor, trade.Opposite(actor), connection, metadata)), async (unit, token) =>
                {
                    try
                    {
                        if (await trades.SettleAsync(trade, token).ConfigureAwait(false)) return true;
                        unit.RequireRollback(new InvalidOperationException("Trade assets or participants changed."));
                        return false;
                    }
                    catch (ContainerAdditionException refusal) when (refusal.Reason == ContainerAdditionFailure.NoRoom)
                    {
                        unit.RequireRollback(refusal);
                        return false;
                    }
                }, ct).ConfigureAwait(false);
        }
        catch
        {
            trades.Invalidate(actor);
            throw;
        }
    }

    internal async ValueTask CloseAsync(PlayerTradeSession trade, bool settled, CancellationToken ct)
    {
        if (!trades.Close(trade)) return;
        if (!trade.Accepted)
        {
            await SendAsync(trade.Inviter, new TradePeerReply(0x000B, [0, 0x85, PeerType]), ct).ConfigureAwait(false);
            return;
        }

        foreach (var member in trade.Participants)
        {
            if (!sessions.IsInWorld(member)) continue;
            var inventory = await inventories.ReadCurrentAsync(member.AccountId, member.CharacterId, ct).ConfigureAwait(false);
            var account = await accounts.LoadAsync(member.AccountId, member.CharacterId, ct).ConfigureAwait(false);
            var main = inventory.Containers.Single(container => container.ContainerType == InventoryContainerType.Main);
            await outbox.RegisterExplicitAsync(member.ConnectionId, new(
                [TradePackets.Closed(settled), new InventoryFullSnapshotMessage(main, inventory, account, clock.GetUtcNow())],
                []), ct).ConfigureAwait(false);
        }
    }

    internal ValueTask SendAsync(SelectedSessionIdentity recipient, IGameServerMessage message, CancellationToken ct) =>
        sessions.IsInWorld(recipient)
            ? outbox.RegisterExplicitAsync(recipient.ConnectionId, new([message], []), ct)
            : ValueTask.CompletedTask;

    private static IReadOnlyList<SelectedSessionRecipient> Recipients(SelectedSessionIdentity actor,
        SelectedSessionIdentity other, NetworkConnectionContext connection, GameMessageMetadata metadata) =>
        [new(actor, connection, metadata), new(other, null)];

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

    private sealed record QuoteResult(PreparedTradeOffer? Offer);
}

internal sealed record TradeMoveResult(GameSlot Destination, SelectedSessionIdentity Opposite, TradeNotice Notice);
