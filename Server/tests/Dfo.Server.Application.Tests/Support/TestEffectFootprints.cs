using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>Explicit non-request input for isolated domain tests, scoped to one invocation.</summary>
internal static class TestEffectFootprints
{
    internal sealed class Lease(IReadOnlyList<EffectFootprintOwner> owners) : IEffectFootprintRecorder, IDisposable
    {
        public Lease(EffectFootprintOwner owner) : this(new[] { owner }) { }
        public Lease(SelectedSessionIdentity owner) : this(new EffectFootprintOwner(owner.AccountId, owner.CharacterId, owner)) { }
        private readonly Guid job = GameplayJob.Current?.Id
            ?? throw new InvalidOperationException("The test explicitly creates its footprint on a job.");
        private bool closed;
        public void DemandEffects(EffectFootprintOwner value)
        {
            if (closed || GameplayJob.Current?.Id != job || !owners.Any(owner => value.AccountId == owner.AccountId && value.CharacterId == owner.CharacterId
                    && (value.Session is null || owner.Session is null || value.Session == owner.Session))
                || value.Session is { } session && (session.AccountId != value.AccountId || session.CharacterId != value.CharacterId))
                throw new InvalidOperationException("The explicit test footprint is closed or has a different owner/job.");
            GameplayJob.DemandHealthy();
        }
        public int RecordedEffects { get; private set; }
        public void RecordEffects(EffectFootprintOwner value, IReadOnlyList<AppliedEffect> applied)
        { DemandEffects(value); RecordedEffects++; }
        public void DemandActive(SelectedSessionIdentity value) => DemandEffects(new(value.AccountId, value.CharacterId, value));
        public void RecordInventorySlots(SelectedSessionIdentity value, IReadOnlyList<BusinessSlot> slots) => DemandActive(value);
        public void RecordRentalState(SelectedSessionIdentity value) => DemandActive(value);
        public void Dispose() => closed = true;
    }

    internal static async ValueTask<T> Run<T>(IEnumerable<SelectedSessionIdentity> owners, Func<IEffectFootprintRecorder, ValueTask<T>> action)
    {
        using var lease = new Lease(owners.Select(owner => new EffectFootprintOwner(owner.AccountId, owner.CharacterId, owner)).ToArray());
        return await action(lease).ConfigureAwait(false);
    }

    internal static ValueTask<T> Run<T>(SelectedSessionIdentity owner, Func<IEffectFootprintRecorder, ValueTask<T>> action) =>
        Run(new EffectFootprintOwner(owner.AccountId, owner.CharacterId, owner), action);

    internal static async ValueTask<T> Run<T>(EffectFootprintOwner owner,
        Func<IEffectFootprintRecorder, ValueTask<T>> action)
    {
        using var lease = new Lease(owner);
        return await action(lease).ConfigureAwait(false);
    }

    // Known direct-handler state, independent of the real network replay ledger.
    internal static async ValueTask Effect(IServiceProvider services, SelectedSessionIdentity owner,
        Dfo.Server.Data.Networking.GameMessageMetadata metadata, ReplayProjectionNeeds minimum, Func<ValueTask> action)
    {
        using var source = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<NetworkCommandAuthorization>(services)
            .Enter(() => true, metadata, NetworkCommandAuthorization.MessageCategory.Effect);
        using var operation = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Dfo.Server.Application.Effects.EffectOperationContext>(services).Enter(
            new(owner, 1, metadata.Selector, metadata.Type, metadata.Sequence, "explicit-test-invocation", 0),
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<SelectedSessionSafety>(services).Capture(owner, metadata), minimum);
        await action().ConfigureAwait(false);
    }

    internal static async ValueTask<T> NonEffect<T>(IServiceProvider services, Dfo.Server.Data.Networking.GameMessageMetadata metadata, Func<ValueTask<T>> action)
    {
        using var source = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<NetworkCommandAuthorization>(services)
            .Enter(() => true, metadata, NetworkCommandAuthorization.MessageCategory.NonEffect);
        return await action().ConfigureAwait(false);
    }
    internal static async ValueTask NonEffect(IServiceProvider services, Dfo.Server.Data.Networking.GameMessageMetadata metadata, Func<ValueTask> action)
    {
        using var source = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<NetworkCommandAuthorization>(services)
            .Enter(() => true, metadata, NetworkCommandAuthorization.MessageCategory.NonEffect);
        await action().ConfigureAwait(false);
    }

    internal static async ValueTask<TResult> Invoke<TCommand, TResult>(ICommandHandler<TCommand, TResult> handler,
        TCommand command, CancellationToken ct)
        where TCommand : class, ICommand<TResult>, IRequestSettlementCommand where TResult : notnull
    {
        using var lease = new Lease(command.Session ?? throw new InvalidOperationException("This test requires an owner."));
        var context = new CommandExecutionContext(Guid.NewGuid(), typeof(TCommand), handler.GetType(), CommandExecutionKind.Transactional);
        context.SupplyLegacyFootprint(lease);
        return await handler.HandleAsync(context, command, ct).ConfigureAwait(false);
    }
}
