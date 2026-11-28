using System.Collections.Immutable;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

internal sealed class LuckyCharacterEventDomainService(IEventPersistence store, IEventPeriodRuleService periods)
    : EntranceEventDomainService(store, periods), ILuckyCharacterEventQuery
{
    // Server management identity. The client activation list carries career rows only.
    internal const int ActivityId = 10976;
    private static readonly ImmutableArray<int> Jobs = [.. Enumerable.Range(0, 14)];

    public override int EventId => ActivityId;
    protected override string Key => "lucky-character";

    protected override EventConfiguration Initial => new(0, "initial", false, "幸运角色", "幸运角色",
        "指定职业在有效期内获得幸运角色福利。", "", "", null, null,
        LuckyCharacter: new(AllJobs: true, Jobs: []));

    protected override EventInfo[] Project(EventConfiguration configuration) => [];

    public async ValueTask<LuckyCharacterPeriod> ReadAtAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        var configuration = await Configuration(cancellationToken).ConfigureAwait(false);
        if (!IsOpen(configuration, at) || configuration.LuckyCharacter is not { } settings)
            return new(configuration.Revision, []);
        return new(configuration.Revision, settings.AllJobs ? Jobs : [.. settings.Jobs]);
    }

    protected override EventConfiguration Configure(EventConfiguration configuration, EventChange change)
    {
        configuration = base.Configure(configuration, change);
        if (change.LuckyCharacter is not { Jobs: { } jobs } settings || jobs.Any(job => !Jobs.Contains(job))
            || jobs.Count != jobs.Distinct().Count() || settings.AllJobs != (jobs.Count == 0))
            throw new ArgumentException("请选择全职业或有效的幸运职业名单。");
        return configuration with { LuckyCharacter = new(settings.AllJobs, jobs.Order().ToArray()) };
    }
}
