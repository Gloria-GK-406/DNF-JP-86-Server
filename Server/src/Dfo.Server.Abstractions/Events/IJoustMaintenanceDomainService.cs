using Dfo.Server.Data.Events;
namespace Dfo.Server.Abstractions.Events;
public interface IJoustMaintenanceDomainService
{
    ValueTask PublishCompletedAsync(JoustState state);
    ValueTask ObserveAsync(JoustState state, bool suppressNotifications);
}
