using Dfo.Server.Data.Events;

namespace Dfo.Server.Abstractions.Events;

public interface IEventPeriodRuleService
{
    bool IsActive(EventConfiguration? configuration, DateTimeOffset at);
}
