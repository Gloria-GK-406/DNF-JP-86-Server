using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpSettlementRuleService(IPvpGradeDefinitionSource grades, IDailyResetRuleService days)
    : IPvpSettlementRuleService
{
    private static readonly PvpChannelEntitlements Unrecorded = new(false, false, 0, 0);
    private static readonly PvpChannelEntitlements Ranked = new(true, true, 50, 10);

    // Every channel ChannelEntryDomainService classifies as a duel channel must have a row here; the host's
    // startup verification asks for each one before any listener starts.
    public PvpChannelEntitlements EntitlementsOf(ChannelEntryIdentity channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return channel.Kind switch
        {
            // A free-duel channel may be one named by channel id, with any environment or none.
            ChannelEntryKind.FreeDuel => Unrecorded,
            ChannelEntryKind.Pvp => channel.LoginEnvironment switch
            {
                8 => Unrecorded,
                24 => Ranked,
                _ => throw new ArgumentOutOfRangeException(nameof(channel), channel.LoginEnvironment,
                    "No duel channel entitlements are defined for this login environment."),
            },
            ChannelEntryKind.General or ChannelEntryKind.SaintHorn or ChannelEntryKind.Raid =>
                throw new ArgumentOutOfRangeException(nameof(channel), channel.Kind, "The channel is not a duel channel."),
        };
    }

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
