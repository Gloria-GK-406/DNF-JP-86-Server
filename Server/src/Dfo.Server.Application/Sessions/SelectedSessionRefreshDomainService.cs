using System.Collections.Immutable;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Sessions;

internal sealed class SelectedSessionRefreshDomainService(ISelectedSessionSnapshotQuery directory,
    ISelectedSessionDomainService sessions, IDailyResetRuleService daily, IPremiumServiceUsageDomainService premium,
    TimeProvider clock) : ISelectedSessionRefreshDomainService
{
    private readonly Dictionary<(SelectedSessionRefreshTopic, SelectedSessionIdentity), SelectedSessionRefreshState> registered = [];
    public DateTimeOffset CurrentPeriod(SelectedSessionRefreshTopic topic)
    {
        if (topic == SelectedSessionRefreshTopic.Premium) return premium.CurrentPeriodStart;
        return daily.PeriodStart(clock.GetUtcNow());
    }
    public SelectedSessionRefreshScan Capture(SelectedSessionRefreshTopic topic)
    {
        GameplayWriteAssertion.Writing(nameof(SelectedSessionRefreshDomainService) + ".Capture");
        var current = directory.Snapshot().ToImmutableArray();
        var active = current.ToHashSet();
        foreach (var key in registered.Keys.Where(key => key.Item1 == topic && !active.Contains(key.Item2)).ToArray()) registered.Remove(key);
        return new(CurrentPeriod(topic), current);
    }
    public bool IsCurrent(SelectedSessionRefreshTopic topic, DateTimeOffset period, SelectedSessionIdentity session) =>
        CurrentPeriod(topic) == period && sessions.IsInWorld(session);
    public SelectedSessionRefreshState? Previous(SelectedSessionRefreshTopic topic, SelectedSessionIdentity session) => registered.GetValueOrDefault((topic, session));
    public bool NeedsRefresh(SelectedSessionRefreshTopic topic, SelectedSessionIdentity session, SelectedSessionRefreshState state) =>
        Previous(topic, session) != state;
    public bool MarkRegistered(SelectedSessionRefreshTopic topic, DateTimeOffset period, SelectedSessionIdentity session, SelectedSessionRefreshState state)
    {
        GameplayWriteAssertion.Writing(nameof(SelectedSessionRefreshDomainService) + ".MarkRegistered");
        if (!IsCurrent(topic, period, session)) return false;
        registered[(topic, session)] = state;
        return true;
    }
}
