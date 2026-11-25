using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpRoomRuleService(IPvpMapDefinitionSource maps) : IPvpRoomRuleService
{
    public bool AcceptsMap(int index) => index >= 0 && index < maps.Load().Order.Count;

    public byte StartMap(int index, int seed)
    {
        if (!AcceptsMap(index))
            throw new ArgumentOutOfRangeException(nameof(index), index, "Map index is outside the archive's map order.");
        return (byte)(index != 0 ? index : new Random(seed).Next(1, maps.Load().Order.Count));
    }

    public bool AcceptsPassword(System.Collections.Immutable.ImmutableArray<byte> password) =>
        PvpRoomRules.AcceptsPassword(password.IsDefault ? 0 : password.Length);
    public TimeSpan FightOverrun => PvpRoomRules.FightOverrun;
    public TimeSpan HeartbeatTimeout => PvpRoomRules.HeartbeatTimeout;
    public TimeSpan RankTimeout => PvpRoomRules.RankTimeout;
    public TimeSpan ResultTimeout(PvpRoomOrigin origin) => origin == PvpRoomOrigin.Matchmaking
        ? PvpRoomRules.MatchmakingResultTimeout
        : PvpRoomRules.ResultTimeout;
    public TimeSpan TimeoutReportLead => PvpRoomRules.TimeoutReportLead;
    public TimeSpan SettlementGiveUp => PvpRoomRules.SettlementGiveUp;
    public TimeSpan InvitationLifetime => PvpRoomRules.InvitationLifetime;
    public bool AllowsClosing(int members, int openSeats) => PvpRoomRules.AllowsClosing(members, openSeats);
    public int? Successor(int ownerSeat, IReadOnlyList<int> remainingSeats) => PvpRoomRules.Successor(ownerSeat, remainingSeats);
}
