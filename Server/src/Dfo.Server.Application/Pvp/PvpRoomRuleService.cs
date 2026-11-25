using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpRoomRuleService : IPvpRoomRuleService
{
    private readonly IPvpMapDefinitionSource maps;
    private readonly IReadOnlyDictionary<PvpBattleMode, IPvpBattleModeRules> modes;

    public PvpRoomRuleService(IPvpMapDefinitionSource maps)
    {
        this.maps = maps;
        // The A21 countdown and KO animation must finish before candidates and relay fighters are assigned.
        TotalMatch = new PvpTotalMatchBattleRules(openingDelay: TimeSpan.FromSeconds(2),
            selectionWindow: TimeSpan.FromSeconds(6), roundIntro: TimeSpan.FromMilliseconds(2700 + 4640),
            knockoutWindow: TimeSpan.FromSeconds(3));
        modes = new Dictionary<PvpBattleMode, IPvpBattleModeRules>
        {
            [PvpBattleMode.Individual] = new PvpIndividualBattleRules(),
            [PvpBattleMode.Team] = new PvpTeamBattleRules(),
            [PvpBattleMode.Relay] = new PvpRelayBattleRules(firstTurnDelay: TimeSpan.FromSeconds(7),
                replacementDelay: TimeSpan.FromSeconds(3), clientBattleTime: TimeSpan.FromMinutes(3),
                clientRoundCountdown: TimeSpan.FromMilliseconds(4640)),
            [PvpBattleMode.TotalMatch] = TotalMatch,
        };
    }

    public IPvpBattleModeRules? FindBattleMode(PvpBattleMode mode) => modes.GetValueOrDefault(mode);
    public IPvpBattleModeRules BattleMode(PvpBattleMode mode) => modes[mode];
    public IPvpTotalMatchBattleRules TotalMatch { get; }

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
    public TimeSpan MatchAdmissionTimeout => TimeSpan.FromSeconds(90);
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
