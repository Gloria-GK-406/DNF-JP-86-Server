using Dfo.Server.Application.Commands.Pipeline;
using Dfo.Server.Application.OnlineUsers;
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
        services.TryAddSingleton<OnlineUserExecutionGate>();
        services.TryAddSingleton<AccountExecutionGate>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<ICommandPipelineBehavior, CommandLoggingBehavior>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<ICommandPipelineBehavior, OnlineUserExecutionBehavior>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<ICommandPipelineBehavior, AccountExecutionBehavior>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<ICommandPipelineBehavior, CommandExecutionPolicyBehavior>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, CommandHandlerRegistryValidationService>());
        return services;
    }

    public static IServiceCollection RegisterCommandHandler<THandler>(
        this IServiceCollection services,
        ApplicationCommandType commandType,
        CommandExecutionKind executionKind,
        UserAccessMode onlineUserAccessMode = UserAccessMode.Unscoped,
        UserAccessMode accountAccessMode = UserAccessMode.Unscoped)
        where THandler : class, ICommandHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<THandler>();
        services.AddSingleton(new CommandHandlerRegistration(
            commandType,
            typeof(THandler),
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
