using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpSettlementRuleService(IPvpGradeDefinitionSource grades, IDailyResetRuleService days)
    : IPvpSettlementRuleService
{
    private static readonly PvpChannelEntitlements Unrecorded = new(false, false, 0, 0);
    private static readonly PvpChannelEntitlements Ranked = new(true, true, 50, 10);

    // Every environment ChannelEntryDomainService classifies as a duel channel must appear here.
    public PvpChannelEntitlements EntitlementsOf(byte loginEnvironment) => loginEnvironment switch
    {
        8 or 13 => Unrecorded,
        24 => Ranked,
        _ => throw new ArgumentOutOfRangeException(nameof(loginEnvironment), loginEnvironment,
            "No duel channel entitlements are defined for this login environment."),
    };

    public PvpSettlementDecision Decide(PvpSettlementFacts facts) => PvpGradeRules.Decide(grades.Load(), facts);
    public byte GradeOf(long experience) => PvpGradeRules.GradeOf(grades.Load(), experience);
    public long LowerBound(byte grade) => PvpGradeRules.Step(grades.Load(), grade).LowerBound;
    public long UpperBound(byte grade) => PvpGradeRules.Step(grades.Load(), grade).UpperBound;

    public TimeSpan BattleTimeLimit(PvpBattleMode mode) => mode switch
    {
        PvpBattleMode.Relay => grades.Load().RelayBattleTime,
        PvpBattleMode.Individual or PvpBattleMode.Team or PvpBattleMode.DeathMatchIndividual
            or PvpBattleMode.DeathMatchTeam or PvpBattleMode.Practice => grades.Load().NormalBattleTime,
    };

    public DateOnly GameDayOf(DateTimeOffset startedAt) => days.PeriodDay(startedAt);
}
