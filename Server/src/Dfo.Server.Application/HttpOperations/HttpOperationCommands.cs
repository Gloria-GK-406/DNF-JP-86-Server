using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.HttpOperations;

/// <summary>
/// A launcher or GM route's gameplay part. It carries transport facts only: no HTTP context, service or
/// executable continuation crosses the queue, and its text never shows the session secret it may carry.
/// </summary>
public abstract record HttpOperationCommand : ICommand<HttpRouteReply>
{
    public sealed override string ToString() => GetType().Name + " { }";
}

public sealed record HttpRouteReply(object? Value = null, Exception? Error = null, string? Outcome = null, string? Reminder = null, bool ExecutorFailed = false);

/// <summary>The route's input could not be parsed; the failure still runs as the route's one CMD.</summary>
public sealed record FailedHttpInputCommand(Exception Cause) : HttpOperationCommand;

internal sealed class FailedHttpInputCommandHandler : ICommandHandler<FailedHttpInputCommand, HttpRouteReply>
{
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, FailedHttpInputCommand command, CancellationToken token)
    {
        if (context.Request is not CommandRequestContext.TrustedInternal { Origin: { Kind: GameplayCallSource.Http or GameplayCallSource.Gm, SourceConnection: null } } request)
            throw new InvalidOperationException("A failed HTTP input requires its trusted origin.");
        request.DemandCurrent();
        if (command.Cause is OperationCanceledException) ExceptionDispatchInfo.Throw(command.Cause);
        return ValueTask.FromResult(new HttpRouteReply(Error: command.Cause));
    }
}

/// <summary>The fixed shape every HTTP operation handler shares: its origin, units and business-failure reply.</summary>
internal static class HttpOperationWork
{
    internal static void DemandOrigin(CommandExecutionContext context, GameplayCallSource source)
    {
        if (context.Request is not CommandRequestContext.TrustedInternal request
            || request.Origin.Kind != source || request.Origin.SourceConnection is not null)
            throw new InvalidOperationException("The HTTP command requires its trusted origin.");
        request.DemandCurrent();
    }

    internal static bool BusinessFailure(Exception error) => error is not OperationCanceledException
        && !TransactionOutcomeUncertainException.Contains(error);

    /// <summary>A business refusal becomes the reply's error; request failures still fail the CMD.</summary>
    internal static async ValueTask<HttpRouteReply> ReplyAsync(CommandExecutionContext context, Func<ValueTask<object>> body)
    {
        try
        {
            var value = await body().ConfigureAwait(false);
            return value as HttpRouteReply ?? new(value);
        }
        catch (Exception error) when (BusinessFailure(error) && context.Request!.Failure is null) { return new(Error: error); }
    }

    internal static async ValueTask<T> WriteAsync<T>(ICommandWorkUnits units, CommandExecutionContext context, string name,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken ct,
        CommandFailureAuditRole audit = CommandFailureAuditRole.None) where T : notnull =>
        Unwrap(await units.WriteAsync(context, new(name, [], FailureAudit: audit), (unit, token) => Action(context, unit, action, token), ct).ConfigureAwait(false));

    internal static async ValueTask<T> ReadAsync<T>(ICommandWorkUnits units, CommandExecutionContext context, string name,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) where T : notnull =>
        Unwrap(await units.ReadOnlyAsync(context, new(name, []), (unit, token) => Action(context, unit, action, token), ct).ConfigureAwait(false));

    private sealed record Stage<T>(T? Value, Exception? Error) where T : notnull;
    private static async ValueTask<Stage<T>> Action<T>(CommandExecutionContext context, CommandWorkUnit unit,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) where T : notnull
    {
        try { return new(await action(ct).ConfigureAwait(false), null); }
        catch (Exception error) when (BusinessFailure(error) && context.Request!.Failure is null && unit.TransactionIsOpen)
        { unit.RequireRollback(error); return new(default, error); }
    }
    private static T Unwrap<T>(Stage<T> result) where T : notnull
    {
        if (result.Error is { } error) ExceptionDispatchInfo.Throw(error);
        return result.Value!;
    }
}

public static class HttpOperationCommandRegistration
{
    /// <summary>Every launcher and GM route's CMD, each named for its own operation.</summary>
    public static IServiceCollection AddHttpOperationCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddInternalCommandHandler<FailedHttpInputCommand, HttpRouteReply, FailedHttpInputCommandHandler>();
        services.AddAccountHttpCommands();
        services.AddGmAdministrationCommands();
        return services;
    }
}
