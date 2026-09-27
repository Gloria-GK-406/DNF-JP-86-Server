using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands.Pipeline;
using Dfo.Server.Application.OnlineUsers;
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
        services.TryAddSingleton<CommandHandlerRegistry>();
        services.TryAddSingleton<ICommandBus, CommandBus>();
        services.TryAddSingleton<NetworkCommandAuthorization>();
        services.TryAddSingleton<OnlineUserExecutionGate>();
        services.TryAddSingleton<SelectedSessionTransitionGate>();
        services.TryAddSingleton<AccountExecutionGate>();
        services.TryAddSingleton<TransactionalExecutionGate>();
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
            typeof(ICommandPipelineBehavior<>), typeof(OnlineUserExecutionBehavior<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(ICommandPipelineBehavior<>), typeof(AccountExecutionBehavior<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(ICommandPipelineBehavior<>), typeof(TransactionalExecutionBehavior<>)));
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
        CommandExecutionKind executionKind,
        UserAccessMode onlineUserAccessMode = UserAccessMode.Unscoped,
        UserAccessMode accountAccessMode = UserAccessMode.Unscoped)
        where TCommand : class, ICommand<TResult>
        where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ICommandHandler<TCommand, TResult>, THandler>();
        services.AddScoped<CommandInvoker<TCommand, TResult>>();
        services.AddSingleton(new CommandHandlerRegistration(
            typeof(TCommand),
            typeof(THandler),
            typeof(CommandInvoker<TCommand, TResult>),
            executionKind,
            onlineUserAccessMode,
            accountAccessMode));
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
