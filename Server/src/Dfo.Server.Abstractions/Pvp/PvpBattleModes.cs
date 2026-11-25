namespace Dfo.Server.Abstractions.Pvp;

/// <summary>The battle mode byte a duel room carries.</summary>
public enum PvpBattleMode : byte
{
    Individual = 1,
    Team = 2,
    Relay = 3,
    DeathMatchIndividual = 4,
    DeathMatchTeam = 5,
    Practice = 6,
    TotalMatch = 8,
}

/// <summary>How a room's seats are split into sides when its mode is chosen.</summary>
public enum PvpTeamAssignment : byte
{
    /// <summary>Every fighter is a side of their own.</summary>
    Individual,

    /// <summary>Seats alternate between two teams.</summary>
    AlternatingTwoTeams,
}

/// <summary>When a battle is over.</summary>
public enum PvpBattleTerminal : byte
{
    LastFighterStanding,
    LastTeamStanding,
}

/// <summary>
/// The answers a battle mode gives to decisions every mode shares, so room and battle code ask
/// the mode instead of testing for particular modes.
/// </summary>
/// <param name="Supported">Whether a room may be switched to this mode at all.</param>
/// <param name="RequiresBalancedTeams">Whether a battle may only start with equally sized sides.</param>
/// <param name="Settles">Whether a finished battle writes results through settlement.</param>
public sealed record PvpBattleModeCapabilities(
    bool Supported,
    PvpTeamAssignment TeamAssignment,
    bool RequiresBalancedTeams,
    PvpBattleTerminal Terminal,
    bool Settles)
{
    public static PvpBattleModeCapabilities Of(PvpBattleMode mode) => mode switch
    {
        PvpBattleMode.Individual => new(true, PvpTeamAssignment.Individual, false,
            PvpBattleTerminal.LastFighterStanding, true),
        PvpBattleMode.Team => new(true, PvpTeamAssignment.AlternatingTwoTeams, true,
            PvpBattleTerminal.LastTeamStanding, true),
        PvpBattleMode.Relay => new(true, PvpTeamAssignment.AlternatingTwoTeams, true,
            PvpBattleTerminal.LastTeamStanding, true),
        PvpBattleMode.TotalMatch => new(true, PvpTeamAssignment.AlternatingTwoTeams, true,
            PvpBattleTerminal.LastTeamStanding, false),
        // The remaining modes do not start or settle a match.
        PvpBattleMode.DeathMatchIndividual => new(false, PvpTeamAssignment.Individual, false,
            PvpBattleTerminal.LastFighterStanding, false),
        PvpBattleMode.DeathMatchTeam => new(false, PvpTeamAssignment.AlternatingTwoTeams, true,
            PvpBattleTerminal.LastTeamStanding, false),
        PvpBattleMode.Practice => new(false, PvpTeamAssignment.Individual, false,
            PvpBattleTerminal.LastFighterStanding, false),
    };
}
