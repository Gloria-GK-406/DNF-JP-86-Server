using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpSettlementRuleService(IPvpGradeDefinitionSource grades, IDailyResetRuleService days)
    : IPvpSettlementRuleService
{
    // Every channel ChannelEntryDomainService classifies as a duel channel must have a row here; the host's
    // startup verification asks for each one before any listener starts.
    public PvpChannelEntitlements EntitlementsOf(ChannelEntryIdentity channel, PvpBattleMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (mode is { } matchMode && PvpTraditionalDuelDomainService.AllowsMatchmaking(channel, matchMode))
            return PvpTraditionalDuelDomainService.Entitlements;
        return channel.Kind switch
        {
            ChannelEntryKind.FreeDuel or ChannelEntryKind.MatchmakingDuel or ChannelEntryKind.TotalMatchDuel => PvpDuelDomainService.Entitlements,
            ChannelEntryKind.TraditionalDuel => PvpTraditionalDuelDomainService.Entitlements,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel.Kind, "No duel channel entitlements are defined for this channel type."),
        };
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
