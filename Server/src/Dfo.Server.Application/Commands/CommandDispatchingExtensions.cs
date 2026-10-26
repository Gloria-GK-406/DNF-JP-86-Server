using Dfo.Server.Application.Effects;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands.Pipeline;
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
        services.TryAddSingleton<ICommandHttpFailureAudit, CommandHttpFailureAudit>();
        services.TryAddSingleton<ICommandSelectedSessionCleanup, CommandSelectedSessionCleanup>();
        services.TryAddSingleton<NetworkCommandAuthorization>();
        services.TryAddSingleton<SelectedSessionSafety>();
        services.TryAddSingleton<SelectedSessionCommandScope>();
        services.TryAddSingleton<EffectOperationContext>();
        services.TryAddSingleton<EffectOperationLedger>();
        // The scope holds the open transaction for the flow the command runs on. Stores are
        // singletons and containers are held per character, so neither can be handed the
        // transaction by injection; both ask this accessor for it instead.
        services.TryAddSingleton<DomainTransactionScope>();
        services.TryAddSingleton<IDomainTransactionAccessor>(
            provider => provider.GetRequiredService<DomainTransactionScope>());
        // Behaviours carry the command's result type, so they register as open generics
        // and the container closes them per result type at resolution.
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(ICommandPipelineBehavior<>), typeof(CommandLoggingBehavior<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(ICommandPipelineBehavior<>), typeof(SelectedSessionExecutionBehavior<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(ICommandPipelineBehavior<>), typeof(SelectedSessionSettlementBehavior<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(ICommandPipelineBehavior<>), typeof(UnitOfWorkBehavior<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(ICommandPipelineBehavior<>), typeof(CommandExecutionPolicyBehavior<>)));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, CommandHandlerRegistryValidationService>());
        return services;
    }

    /// <summary>
    /// Registers the handler for a command. The three type arguments must agree: the
    /// command must declare this result, and the handler must handle this command and
    /// produce this result, or the call does not compile.
    /// </summary>
    public static IServiceCollection AddCommandHandler<TCommand, TResult, THandler>(
        this IServiceCollection services,
        CommandExecutionKind executionKind)
        where TCommand : class, ICommand<TResult>
        where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        return Add<TCommand, TResult, THandler>(services, executionKind, CommandRouteKind.LegacyFineGrained, null);
    }

    public static IServiceCollection AddProtocolCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services,
        params ProtocolCommandMessageBinding[] messages)
        where TCommand : class, IProtocolCommand<TResult> where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult> =>
        Add<TCommand, TResult, THandler>(services, CommandExecutionKind.Direct, CommandRouteKind.Protocol, [.. messages]);

    public static IServiceCollection AddProtocolCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services,
        IReadOnlyCollection<CommandRegistrationExit> exits, params ProtocolCommandMessageBinding[] messages)
        where TCommand : class, IProtocolCommand<TResult> where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult> =>
        Add<TCommand, TResult, THandler>(services, CommandExecutionKind.Direct, CommandRouteKind.Protocol, [.. messages], exits);

    public static IServiceCollection AddProtocolCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services,
        CommandSelfSettlementCapability capability, params ProtocolCommandMessageBinding[] messages)
        where TCommand : class, IProtocolCommand<TResult> where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult> =>
        Add<TCommand, TResult, THandler>(services, CommandExecutionKind.Direct, CommandRouteKind.Protocol, [.. messages], selfSettlement: capability);

    public static IServiceCollection AddProtocolCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services,
        CommandSelectedSessionCleanupCapability capability, params ProtocolCommandMessageBinding[] messages)
        where TCommand : class, IProtocolCommand<TResult> where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult> =>
        Add<TCommand, TResult, THandler>(services, CommandExecutionKind.Direct, CommandRouteKind.Protocol, [.. messages], selectedSessionCleanup: capability);

    public static IServiceCollection AddInternalCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services)
        where TCommand : class, ICommand<TResult> where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult> =>
        Add<TCommand, TResult, THandler>(services, CommandExecutionKind.Direct, CommandRouteKind.Internal, null);

    private static IServiceCollection Add<TCommand, TResult, THandler>(IServiceCollection services,
        CommandExecutionKind kind, CommandRouteKind route, IReadOnlyList<ProtocolCommandMessageBinding>? messages, IReadOnlyCollection<CommandRegistrationExit>? exits = null,
        CommandSelfSettlementCapability selfSettlement = CommandSelfSettlementCapability.None,
        CommandSelectedSessionCleanupCapability selectedSessionCleanup = CommandSelectedSessionCleanupCapability.None)
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
            kind, route, messages, exits, selfSettlement, selectedSessionCleanup).Freeze());
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
