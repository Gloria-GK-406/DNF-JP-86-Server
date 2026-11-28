using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Trade;

public enum TradeAction { Cancel, Register, Ready, Confirm }
public enum TradeAdvance { Rejected, Changed, BothRegistered, Settle, Canceled }
public sealed record TradeSource(BusinessSlot? Slot, int ItemId);
public sealed record TradeOfferChange(int TradeSlot, TradeSource Source, int Count, ItemSnapshot? Snapshot);

/// <summary>An offer quoted by the domain and applied after the inventory work unit commits.</summary>
public interface IPreparedTradeOffer;

public interface IPlayerTradeSession
{
    SelectedSessionIdentity Inviter { get; }
    SelectedSessionIdentity Invitee { get; }
    IEnumerable<SelectedSessionIdentity> Participants { get; }
    int Peer { get; }
    bool Accepted { get; }
    SelectedSessionIdentity Opposite(SelectedSessionIdentity actor);
}

public interface IPlayerTradeDomainService
{
    IPlayerTradeSession? Find(SelectedSessionIdentity actor);
    ValueTask<bool> CanInviteAsync(SelectedSessionIdentity actor, SelectedSessionIdentity target, CancellationToken ct);
    IPlayerTradeSession? Invite(SelectedSessionIdentity actor, SelectedSessionIdentity target, int peer);
    bool IsUsable(IPlayerTradeSession trade);
    ValueTask<bool> CanAcceptAsync(IPlayerTradeSession trade, CancellationToken ct);
    bool Accept(IPlayerTradeSession trade);
    ValueTask<IPreparedTradeOffer?> PrepareOfferAsync(IPlayerTradeSession trade, SelectedSessionIdentity actor,
        TradeSource source, int count, CancellationToken ct);
    TradeOfferChange? ApplyOffer(IPlayerTradeSession trade, SelectedSessionIdentity actor, IPreparedTradeOffer prepared);
    TradeOfferChange? Withdraw(IPlayerTradeSession trade, SelectedSessionIdentity actor,
        int index, int itemId, int count, bool crystalDestination);
    TradeAdvance Advance(IPlayerTradeSession trade, SelectedSessionIdentity actor, TradeAction action);
    ValueTask<bool> SettleAsync(IPlayerTradeSession trade, CancellationToken ct);
    void Invalidate(SelectedSessionIdentity actor);
    IReadOnlyList<IPlayerTradeSession> InvalidTrades();
    bool Close(IPlayerTradeSession trade);
}
