using Dfo.Server.Application.Effects;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Commands;

public static class CommandDispatchingExtensions
{
    public static IServiceCollection AddCommandDispatching(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddGameplayExecution();
        services.TryAddSingleton<CommandHandlerRegistry>();
        services.TryAddSingleton<ICommandBus, CommandBus>();
        services.TryAddSingleton<CommandJobEntry>();
        services.TryAddSingleton<ICommandWorkUnits, CommandWorkUnits>();
        services.TryAddSingleton<ICommandFailureAudit, CommandFailureAudit>();
        services.TryAddSingleton<ICommandSelectedSessionCleanup, CommandSelectedSessionCleanup>();
        services.TryAddSingleton<SelectedSessionSafety>();
        services.TryAddSingleton<EffectOperationLedger>();
        // The scope holds the open transaction for the flow the command runs on. Stores are
        // singletons and containers are held per character, so neither can be handed the
        // transaction by injection; both ask this accessor for it instead.
        services.TryAddSingleton<DomainTransactionScope>();
        services.TryAddSingleton<IDomainTransactionAccessor>(
            provider => provider.GetRequiredService<DomainTransactionScope>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, CommandHandlerRegistryValidationService>());
        return services;
    }

    public static IServiceCollection AddProtocolCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services,
        params ProtocolCommandMessageBinding[] messages)
        where TCommand : class, IProtocolCommand<TResult> where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult> =>
        Add<TCommand, TResult, THandler>(services, CommandRouteKind.Protocol, [.. messages]);

    public static IServiceCollection AddProtocolCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services,
        IReadOnlyCollection<CommandRegistrationExit> exits, params ProtocolCommandMessageBinding[] messages)
        where TCommand : class, IProtocolCommand<TResult> where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult> =>
        Add<TCommand, TResult, THandler>(services, CommandRouteKind.Protocol, [.. messages], exits);

    public static IServiceCollection AddProtocolCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services,
        CommandSelfSettlementCapability capability, params ProtocolCommandMessageBinding[] messages)
        where TCommand : class, IProtocolCommand<TResult> where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult> =>
        Add<TCommand, TResult, THandler>(services, CommandRouteKind.Protocol, [.. messages], selfSettlement: capability);

    public static IServiceCollection AddProtocolCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services,
        CommandSelectedSessionCleanupCapability capability, params ProtocolCommandMessageBinding[] messages)
        where TCommand : class, IProtocolCommand<TResult> where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult> =>
        Add<TCommand, TResult, THandler>(services, CommandRouteKind.Protocol, [.. messages], selectedSessionCleanup: capability);

    public static IServiceCollection AddInternalCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services,
        ReadOnlyActionCancellationCapability readOnlyActionCancellation = ReadOnlyActionCancellationCapability.None,
        InternalActionCancellationCapability internalActionCancellation = InternalActionCancellationCapability.None,
        CommandFailureAuditCapability failureAudit = CommandFailureAuditCapability.None)
        where TCommand : class, ICommand<TResult> where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult> =>
        Add<TCommand, TResult, THandler>(services, CommandRouteKind.Internal, null, readOnlyActionCancellation: readOnlyActionCancellation,
            internalActionCancellation: internalActionCancellation, failureAudit: failureAudit);

    private static IServiceCollection Add<TCommand, TResult, THandler>(IServiceCollection services,
        CommandRouteKind route, IReadOnlyList<ProtocolCommandMessageBinding>? messages, IReadOnlyCollection<CommandRegistrationExit>? exits = null,
        CommandSelfSettlementCapability selfSettlement = CommandSelfSettlementCapability.None,
        CommandSelectedSessionCleanupCapability selectedSessionCleanup = CommandSelectedSessionCleanupCapability.None,
        ReadOnlyActionCancellationCapability readOnlyActionCancellation = ReadOnlyActionCancellationCapability.None,
        InternalActionCancellationCapability internalActionCancellation = InternalActionCancellationCapability.None,
        CommandFailureAuditCapability failureAudit = CommandFailureAuditCapability.None)
        where TCommand : class, ICommand<TResult> where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ICommandHandler<TCommand, TResult>, THandler>();
        services.AddScoped<CommandInvoker<TCommand, TResult>>();
        services.AddSingleton(new CommandHandlerRegistration(
            typeof(TCommand),
            typeof(THandler),
            typeof(CommandInvoker<TCommand, TResult>),
            route, messages, exits, selfSettlement, selectedSessionCleanup, readOnlyActionCancellation, internalActionCancellation, failureAudit).Freeze());
        return services;
    }
}

internal sealed class CommandHandlerRegistryValidationService(CommandHandlerRegistry registry) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = registry;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
