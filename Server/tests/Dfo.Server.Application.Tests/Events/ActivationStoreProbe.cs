using Dfo.Server.Abstractions.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Events;

internal static class ActivationStoreProbe
{
    public static void Install(IServiceCollection services, Action<int, ManagedEventActivationState> beforeSave,
        Action<int, ManagedEventActivationState>? afterSave = null)
    {
        var original = services.Single(x => x.ServiceType == typeof(IManagedEventActivationPersistence));
        services.Remove(original);
        services.AddSingleton<IManagedEventActivationPersistence>(provider => new Probe(
            (IManagedEventActivationPersistence)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!),
            beforeSave, afterSave));
    }

    private sealed class Probe(IManagedEventActivationPersistence inner, Action<int, ManagedEventActivationState> before,
        Action<int, ManagedEventActivationState>? after) : IManagedEventActivationPersistence
    {
        public ValueTask<ManagedEventActivationState?> ReadAsync(int id, CancellationToken ct) => inner.ReadAsync(id, ct);
        public async ValueTask SaveAsync(int id, ManagedEventActivationState state, CancellationToken ct)
        {
            before(id, state);
            await inner.SaveAsync(id, state, ct);
            after?.Invoke(id, state);
        }
    }
}
