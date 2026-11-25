using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpSettlementRuleService(IPvpGradeDefinitionSource grades, IDailyResetRuleService days,
    PvpMatchmakingRuleService matchmakingRules)
    : IPvpSettlementRuleService
{
    private static readonly PvpChannelEntitlements NoRecord = new(false, false, 0, 0);
    private static readonly PvpChannelEntitlements Traditional = new(true, true, 50, 10);
    private static readonly PvpChannelEntitlements Colosseum = new(true, true, 50, 10);

    // Every channel ChannelEntryDomainService classifies as a duel channel must have a row here; the host's
    // startup verification asks for each one before any listener starts.
    public PvpChannelEntitlements EntitlementsOf(ChannelEntryIdentity channel, PvpBattleMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (mode is { } matchMode && matchmakingRules.AllowsTotalMatchQueue(channel, matchMode))
            return Traditional;
        return channel.Kind switch
        {
            ChannelEntryKind.FreeDuel or ChannelEntryKind.MatchmakingDuel or ChannelEntryKind.TotalMatchDuel => NoRecord,
            ChannelEntryKind.TraditionalDuel => Traditional,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel.Kind, "No duel channel entitlements are defined for this channel type."),
        };
    }

    public PvpSettlementParticipants ParticipantsFor(PvpMatchConclusion conclusion)
    {
        var entitlements = EntitlementsOf(conclusion.Channel, conclusion.Mode);
        var totalMatch = conclusion.Mode == PvpBattleMode.TotalMatch;
        var traditional = conclusion.Business == PvpDuelBusiness.Traditional;
        var traditionalRecords = traditional && entitlements.RecordsResult
            && (!totalMatch || !conclusion.TotalMatchAppearances.IsDefaultOrEmpty
                && !conclusion.Fighters.All(fighter => fighter.Fled)
                && conclusion.Terminal.Kind != PvpMatchOutcomeKind.Cancelled);
        var individualColosseum = conclusion.Mode == PvpBattleMode.Individual
            && !conclusion.ColosseumRuns.IsEmpty && matchmakingRules.AppliesTo(conclusion);
        var participants = new List<PvpSettlementParticipant>(conclusion.Fighters.Length);
        foreach (var fighter in conclusion.Fighters)
        {
            var outcome = OutcomeOf(conclusion, fighter);
            if (traditionalRecords && totalMatch)
            {
                var appearances = conclusion.TotalMatchAppearances.Where(role => role.Seat == fighter.Seat).ToArray();
                foreach (var role in appearances)
                    participants.Add(new(fighter, role.Character, role.Kills, role.Deaths, outcome, entitlements));
                // The representative still needs a result-screen row when a reserve fought in their place.
                if (!appearances.Any(role => role.Character == fighter.CharacterId))
                    participants.Add(new(fighter, fighter.CharacterId, 0, 0, outcome, NoRecord));
            }
            else
            {
                var fighterEntitlements = individualColosseum && conclusion.ColosseumRuns.ContainsKey(fighter.Seat)
                    ? Colosseum : traditionalRecords ? entitlements : NoRecord;
                participants.Add(new(fighter, fighter.CharacterId, fighter.Kills, fighter.Deaths, outcome, fighterEntitlements));
            }
        }
        return new(traditionalRecords || individualColosseum, participants.AsReadOnly());
    }

    public PvpOutcome OutcomeOf(PvpMatchConclusion conclusion, PvpFighter fighter)
    {
        if (fighter.Fled) return PvpOutcome.Loss;
        if (conclusion.Terminal is not { Kind: PvpMatchOutcomeKind.Decided, Winner: { } winner }) return PvpOutcome.Draw;
        var side = PvpBattleModeCapabilities.Of(conclusion.Mode).Terminal switch
        {
            PvpBattleTerminal.LastFighterStanding => fighter.Seat,
            PvpBattleTerminal.LastTeamStanding => (int)fighter.Team,
        };
        return side == winner ? PvpOutcome.Win : PvpOutcome.Loss;
    }

    public PvpSettlementDecision Decide(PvpSettlementFacts facts) => PvpGradeRules.Decide(grades.Load(), facts);
    public byte GradeOf(long experience) => PvpGradeRules.GradeOf(grades.Load(), experience);
    public byte MaximumGrade => grades.Load().MaximumGrade;
    public long LowerBound(byte grade) => PvpGradeRules.Step(grades.Load(), grade).LowerBound;
    public long UpperBound(byte grade) => PvpGradeRules.Step(grades.Load(), grade).UpperBound;

    public TimeSpan BattleTimeLimit(PvpBattleMode mode) => mode switch
    {
        PvpBattleMode.Relay => grades.Load().RelayBattleTime,
        PvpBattleMode.Individual or PvpBattleMode.Team or PvpBattleMode.DeathMatchIndividual
            or PvpBattleMode.DeathMatchTeam or PvpBattleMode.Practice => grades.Load().NormalBattleTime,
        // The free-duel client uses the tournament relay entry when it starts mode 8.
        PvpBattleMode.TotalMatch => grades.Load().TournamentRelayBattleTime ?? TimeSpan.FromMinutes(3),
    };

    public DateOnly GameDayOf(DateTimeOffset startedAt) => days.PeriodDay(startedAt);
}
