using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

internal sealed class EventPeriodRuleService : IEventPeriodRuleService
{
    public bool IsActive(EventConfiguration? configuration, DateTimeOffset at) =>
        configuration is { Enabled: true, StartsAt: { } start, EndsAt: { } end }
        && start <= at && at < end;
}
