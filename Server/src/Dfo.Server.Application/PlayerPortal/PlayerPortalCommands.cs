using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.PlayerPortal;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.HttpOperations;
using Dfo.Server.Data.PlayerPortal;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.PlayerPortal;

/// <summary>Opens a player session for a password checked off the executor (SD-1: the switch is judged again in here).</summary>
public sealed record LoginPlayerPortalCommand(PlayerCredentialCheck Check) : HttpOperationCommand;
public sealed record LogoutPlayerPortalCommand(string Secret) : HttpOperationCommand;
public sealed record QueryPlayerMeCommand(string Secret) : HttpOperationCommand;

public sealed record QueryPlayerPortalSettingsCommand(string GmSecret) : HttpOperationCommand;
public sealed record ChangePlayerPortalSettingsCommand(string GmSecret, PlayerSettingsChange Change) : HttpOperationCommand;
public sealed record QueryPlayerPortalSessionsCommand(string GmSecret) : HttpOperationCommand;
/// <summary>Ends the sessions of <see cref="AccountId"/>, or of every player when it is null.</summary>
public sealed record KickPlayerPortalSessionsCommand(string GmSecret, int? AccountId) : HttpOperationCommand;

/// <summary>
/// The player routes run as GM-source CMDs (they share the GmWeb listener). A refusal is the reply's error; the login
/// answers with its status instead, because its failure audit has to commit.
/// </summary>
internal sealed class PlayerPortalCommandHandler(IPlayerPortalAccessDomainService access, IPlayerPortalAccountDomainService account,
    IPlayerPortalAdministrationDomainService administration, ICommandWorkUnits units)
    : ICommandHandler<LoginPlayerPortalCommand, HttpRouteReply>, ICommandHandler<LogoutPlayerPortalCommand, HttpRouteReply>,
      ICommandHandler<QueryPlayerMeCommand, HttpRouteReply>, ICommandHandler<QueryPlayerPortalSettingsCommand, HttpRouteReply>,
      ICommandHandler<ChangePlayerPortalSettingsCommand, HttpRouteReply>, ICommandHandler<QueryPlayerPortalSessionsCommand, HttpRouteReply>,
      ICommandHandler<KickPlayerPortalSessionsCommand, HttpRouteReply>
{
    public async ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, LoginPlayerPortalCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return new(await HttpOperationWork.WriteAsync(units, context, "login-player-portal", t => access.LoginAsync(command.Check, t), token).ConfigureAwait(false));
    }
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, LogoutPlayerPortalCommand command, CancellationToken token) =>
        Write(context, "logout-player-portal", async t => { await access.LogoutAsync(command.Secret, t).ConfigureAwait(false); return true; }, token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryPlayerMeCommand command, CancellationToken token) =>
        Write(context, "query-player-me", t => account.MeAsync(command.Secret, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryPlayerPortalSettingsCommand command, CancellationToken token) =>
        Write(context, "query-player-portal-settings", t => administration.SettingsAsync(command.GmSecret, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, ChangePlayerPortalSettingsCommand command, CancellationToken token) =>
        Write(context, "change-player-portal-settings", t => administration.ChangeSettingsAsync(command.GmSecret, command.Change, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryPlayerPortalSessionsCommand command, CancellationToken token) =>
        Write(context, "query-player-portal-sessions", t => administration.SessionsAsync(command.GmSecret, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, KickPlayerPortalSessionsCommand command, CancellationToken token) =>
        Write(context, "kick-player-portal-sessions", async t => new PlayerSessionsKicked(await administration.KickAsync(command.GmSecret, command.AccountId, t).ConfigureAwait(false)), token);

    private ValueTask<HttpRouteReply> Write<T>(CommandExecutionContext context, string name, Func<CancellationToken, ValueTask<T>> action, CancellationToken token)
        where T : notnull
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return HttpOperationWork.ReplyAsync(context, async () => await HttpOperationWork.WriteAsync(units, context, name, action, token).ConfigureAwait(false));
    }
}

public static class PlayerPortalCommandRegistration
{
    /// <summary>Each player and player-portal administration route's CMD.</summary>
    public static IServiceCollection AddPlayerPortalCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddInternalCommandHandler<LoginPlayerPortalCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        services.AddInternalCommandHandler<LogoutPlayerPortalCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        services.AddInternalCommandHandler<QueryPlayerMeCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        services.AddInternalCommandHandler<QueryPlayerPortalSettingsCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        services.AddInternalCommandHandler<ChangePlayerPortalSettingsCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        services.AddInternalCommandHandler<QueryPlayerPortalSessionsCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        services.AddInternalCommandHandler<KickPlayerPortalSessionsCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        return services;
    }
}
