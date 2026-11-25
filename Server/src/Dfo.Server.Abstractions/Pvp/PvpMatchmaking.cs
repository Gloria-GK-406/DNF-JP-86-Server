using System.Collections.Immutable;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Pvp;

/// <summary>The assigned seats' acknowledgements before an assigned duel starts.</summary>
public sealed record PvpMatchAdmission(DateTimeOffset Deadline, byte Participants, byte Confirmed = 0, byte Loaded = 0,
    byte Linked = 0)
{
    private ImmutableArray<byte> PeerLinks { get; init; } = [0, 0, 0, 0, 0, 0, 0, 0];
    public ImmutableArray<PvpTotalMatchSide> TotalMatchSides { get; init; } = [];
    public DateTimeOffset PreparedAt { get; init; }

    public static int FighterCount(PvpBattleMode mode) => mode switch
    {
        PvpBattleMode.Individual or PvpBattleMode.TotalMatch => 2,
        PvpBattleMode.Relay => 6,
        PvpBattleMode.Team or PvpBattleMode.DeathMatchIndividual or PvpBattleMode.DeathMatchTeam
            or PvpBattleMode.Practice => 0,
    };

    public bool Preloading => Confirmed == Participants;

    public PvpMatchAdmission ReportLinks(int seat, byte readyPeers)
    {
        var seatBit = (byte)(1 << seat);
        if ((Loaded & seatBit) == 0) return this;
        var expected = (byte)(Participants & ~seatBit);
        var previous = PeerLinks[seat];
        var current = (byte)(previous | (readyPeers & expected));
        if (current == previous) return this;
        return this with
        {
            PeerLinks = PeerLinks.SetItem(seat, current),
            Linked = current == expected ? (byte)(Linked | seatBit) : Linked,
        };
    }
}

public enum PvpMatchmakingDecision
{
    StaleRequest,
    InvalidRequest,
    NoSelectedCharacter,
    UnsupportedMode,
    ServerClosing,
    PriorResultPending,
    UnavailableInLobby,
    ChannelUnavailable,
    ModeUnavailableInChannel,
    ColosseumStateMismatch,
    CharacterProfileUnavailable,
    InvalidTotalMatchLineup,
    MissingTotalMatchEquipment,
    Waiting,
    Paired,
}

public sealed record PvpMatchmakingQueueResult(PvpMatchmakingDecision Decision, PvpRoomOutcome.Changed? Pair = null)
{
    public bool Accepted => Decision is PvpMatchmakingDecision.Waiting or PvpMatchmakingDecision.Paired;
}
public sealed record PvpMatchmakingCounts(int Individual, int Relay, int? TotalMatch = null);

public interface IPvpMatchmakingDomainService
{
    ValueTask<PvpMatchmakingQueueResult> EnqueueAsync(SelectedSessionIdentity session, PvpBattleMode mode,
        ImmutableArray<CharacterCareer> excludedCareers, CancellationToken cancellationToken, bool colosseum = false);
    bool CanEnterColosseum(SelectedSessionIdentity session);
    ValueTask<bool> CancelAsync(SelectedSessionIdentity session, PvpBattleMode mode, CancellationToken cancellationToken);
    ValueTask<PvpMatchmakingCounts?> WaitingCountsAsync(SelectedSessionIdentity session, CancellationToken cancellationToken);
    ValueTask<PvpRoomOutcome> ConfirmOpponentAsync(
        SelectedSessionIdentity session, uint roomNumber, CancellationToken cancellationToken);
    ValueTask<PvpRoomOutcome> ReportTransportAsync(
        SelectedSessionIdentity session, byte? readyPeers, CancellationToken cancellationToken);
}
