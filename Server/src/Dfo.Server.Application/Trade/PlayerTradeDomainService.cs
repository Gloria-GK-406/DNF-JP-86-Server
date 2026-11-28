using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Trade;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Trade;

internal sealed record TradeOffer(TradeSource Source, int Count, ContainerId Container, ItemSnapshot? Snapshot);
internal sealed class PreparedTradeOffer : IPreparedTradeOffer
{
    internal PreparedTradeOffer(int slot, TradeOffer offer) => (Slot, Offer) = (slot, offer);
    internal int Slot { get; }
    internal TradeOffer Offer { get; }
}

internal sealed class PlayerTradeSession : IPlayerTradeSession
{
    internal PlayerTradeSession(SelectedSessionIdentity inviter, SelectedSessionIdentity invitee,
        int peer, DateTimeOffset expiresAt)
    {
        Inviter = inviter;
        Invitee = invitee;
        Peer = peer;
        ExpiresAt = expiresAt;
    }

    public SelectedSessionIdentity Inviter { get; }
    public SelectedSessionIdentity Invitee { get; }
    public IEnumerable<SelectedSessionIdentity> Participants => [Inviter, Invitee];
    public int Peer { get; }
    internal DateTimeOffset ExpiresAt { get; }
    public bool Accepted { get; internal set; }
    internal bool Invalidated { get; set; }
    internal TradeSide Left { get; } = new();
    internal TradeSide Right { get; } = new();

    public SelectedSessionIdentity Opposite(SelectedSessionIdentity actor) =>
        actor == Inviter ? Invitee : Inviter;

    internal TradeSide Side(SelectedSessionIdentity actor) => actor == Inviter ? Left : Right;
}

internal enum TradePhase { Editing, Registered, Ready, Confirmed }

internal sealed class TradeSide
{
    internal Dictionary<int, TradeOffer> Offers { get; } = [];
    internal TradePhase Phase { get; set; }
}

/// <summary>Owns the transient negotiation. All mutations run on the gameplay executor.</summary>
public sealed class PlayerTradeDomainService(
    ISelectedSessionDomainService sessions, ITownAreaDomainService towns,
    ICharacterRelationQueryDomainService relations, PlayerTradeAssets assets, TimeProvider clock) : IPlayerTradeDomainService
{
    private readonly Dictionary<SelectedSessionIdentity, PlayerTradeSession> active = [];

    public IPlayerTradeSession? Find(SelectedSessionIdentity actor) => active.GetValueOrDefault(actor);

    public async ValueTask<bool> CanInviteAsync(SelectedSessionIdentity actor, SelectedSessionIdentity target,
        CancellationToken ct) =>
        Find(actor) is null && Find(target) is null && CanMeet(actor, target)
        && !await BlockedAsync(actor, target, ct).ConfigureAwait(false);

    public IPlayerTradeSession? Invite(SelectedSessionIdentity actor, SelectedSessionIdentity target, int peer)
    {
        GameplayWriteAssertion.Writing(nameof(Invite));
        if (Find(actor) is not null || Find(target) is not null || !CanMeet(actor, target)) return null;
        var trade = new PlayerTradeSession(actor, target, peer, clock.GetUtcNow().AddMinutes(1));
        active.Add(actor, trade);
        active.Add(target, trade);
        return trade;
    }

    public async ValueTask<bool> CanAcceptAsync(IPlayerTradeSession trade, CancellationToken ct) =>
        !trade.Accepted && IsUsable(trade)
        && !await BlockedAsync(trade.Inviter, trade.Invitee, ct).ConfigureAwait(false);

    public bool Accept(IPlayerTradeSession candidate)
    {
        GameplayWriteAssertion.Writing(nameof(Accept));
        if (candidate is not PlayerTradeSession trade || trade.Accepted || !IsUsable(trade)) return false;
        trade.Accepted = true;
        return true;
    }

    public async ValueTask<IPreparedTradeOffer?> PrepareOfferAsync(IPlayerTradeSession candidate, SelectedSessionIdentity actor,
        TradeSource source, int count, CancellationToken ct)
    {
        if (candidate is not PlayerTradeSession trade || !CanEdit(trade, actor) || count <= 0) return null;
        var offers = trade.Side(actor).Offers;
        var isGold = source.Slot is null;
        var index = isGold ? 0 : Enumerable.Range(3, 9).FirstOrDefault(slot => !offers.ContainsKey(slot), -1);
        if (index < 0 || !isGold && offers.Values.Any(offer => offer.Source.Slot == source.Slot)) return null;
        var total = (long)count + (isGold && offers.TryGetValue(0, out var gold) ? gold.Count : 0);
        if (total > int.MaxValue) return null;
        var offer = await assets.QuoteAsync(actor, source, (int)total, ct).ConfigureAwait(false);
        return offer is null ? null : new PreparedTradeOffer(index, offer);
    }

    public TradeOfferChange? ApplyOffer(IPlayerTradeSession candidate, SelectedSessionIdentity actor, IPreparedTradeOffer quote)
    {
        GameplayWriteAssertion.Writing(nameof(ApplyOffer));
        if (candidate is not PlayerTradeSession trade || quote is not PreparedTradeOffer prepared
            || !CanEdit(trade, actor)) return null;
        var offer = prepared.Offer;
        trade.Side(actor).Offers[prepared.Slot] = offer;
        return new(prepared.Slot, offer.Source, offer.Count, offer.Snapshot);
    }

    public TradeOfferChange? Withdraw(IPlayerTradeSession candidate, SelectedSessionIdentity actor,
        int index, int itemId, int count, bool crystalDestination)
    {
        GameplayWriteAssertion.Writing(nameof(Withdraw));
        if (candidate is not PlayerTradeSession trade || !CanEdit(trade, actor) || count <= 0) return null;
        var offers = trade.Side(actor).Offers;
        if (!offers.TryGetValue(index, out var offer)
            || offer.Source.ItemId != itemId || count > offer.Count
            || crystalDestination && offer.Source.Slot?.Space != BusinessInventorySpace.SharedMaterial) return null;
        var left = offer.Count - count;
        if (left == 0) offers.Remove(index);
        else offers[index] = offer with { Count = left };
        return new(index, offer.Source, left, offer.Snapshot);
    }

    public TradeAdvance Advance(IPlayerTradeSession candidate, SelectedSessionIdentity actor, TradeAction action)
    {
        GameplayWriteAssertion.Writing(nameof(Advance));
        if (action == TradeAction.Cancel || candidate is not PlayerTradeSession trade || !IsUsable(trade)) return TradeAdvance.Canceled;
        if (!trade.Accepted) return TradeAdvance.Rejected;
        var own = trade.Side(actor);
        var other = trade.Side(trade.Opposite(actor));
        switch (action)
        {
            case TradeAction.Register when own.Phase == TradePhase.Editing:
                own.Phase = TradePhase.Registered;
                return other.Phase == TradePhase.Registered ? TradeAdvance.BothRegistered : TradeAdvance.Changed;
            case TradeAction.Ready when own.Phase == TradePhase.Registered && other.Phase >= TradePhase.Registered:
                own.Phase = TradePhase.Ready;
                return TradeAdvance.Changed;
            case TradeAction.Confirm when own.Phase == TradePhase.Ready && other.Phase >= TradePhase.Ready:
                own.Phase = TradePhase.Confirmed;
                return other.Phase == TradePhase.Confirmed ? TradeAdvance.Settle : TradeAdvance.Changed;
            default:
                return TradeAdvance.Rejected;
        }
    }

    public async ValueTask<bool> SettleAsync(IPlayerTradeSession candidate, CancellationToken ct) =>
        candidate is PlayerTradeSession trade && trade.Left.Phase == TradePhase.Confirmed && trade.Right.Phase == TradePhase.Confirmed
            && IsUsable(trade) && !await BlockedAsync(trade.Inviter, trade.Invitee, ct).ConfigureAwait(false)
            && await assets.ExchangeAsync(trade, ct).ConfigureAwait(false);

    public bool IsUsable(IPlayerTradeSession candidate) => candidate is PlayerTradeSession trade && !trade.Invalidated
        && ReferenceEquals(Find(trade.Inviter), trade)
        && (trade.Accepted || trade.ExpiresAt > clock.GetUtcNow()) && CanMeet(trade.Inviter, trade.Invitee);

    public void Invalidate(SelectedSessionIdentity actor)
    {
        GameplayWriteAssertion.Writing(nameof(Invalidate));
        if (active.TryGetValue(actor, out var trade)) trade.Invalidated = true;
    }

    public IReadOnlyList<IPlayerTradeSession> InvalidTrades() =>
        [.. active.Values.Distinct().Where(trade => !IsUsable(trade))];

    public bool Close(IPlayerTradeSession trade)
    {
        GameplayWriteAssertion.Writing(nameof(Close));
        if (!ReferenceEquals(Find(trade.Inviter), trade)) return false;
        active.Remove(trade.Inviter);
        active.Remove(trade.Invitee);
        return true;
    }

    private bool CanEdit(PlayerTradeSession trade, SelectedSessionIdentity actor) =>
        IsUsable(trade) && trade.Accepted && trade.Side(actor).Phase == TradePhase.Editing;

    private bool CanMeet(SelectedSessionIdentity left, SelectedSessionIdentity right) =>
        left.AccountId != right.AccountId && sessions.IsInWorld(left) && sessions.IsInWorld(right)
        && towns.Find(left.CharacterId) is { } a && a.Session == left
        && towns.Find(right.CharacterId) is { } b && b.Session == right
        && a.Area.ChannelId == b.Area.ChannelId;

    private async ValueTask<bool> BlockedAsync(SelectedSessionIdentity left, SelectedSessionIdentity right, CancellationToken ct) =>
        await relations.ExistsAsync(left.CharacterId, right.CharacterId, CharacterRelationKind.Blacklist, ct).ConfigureAwait(false)
        || await relations.ExistsAsync(right.CharacterId, left.CharacterId, CharacterRelationKind.Blacklist, ct).ConfigureAwait(false);
}
