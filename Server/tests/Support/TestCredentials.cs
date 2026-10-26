using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Testing;

/// <summary>
/// The launcher's registration and login as production composes them (RQ-007): the credential reader
/// hashes or verifies off the executor, then one command writes. Tests call this from their own thread,
/// which is off the executor; the HTTP entry evidence is in <c>CredentialEntryHttpTests</c>.
/// </summary>
internal static class TestCredentials
{
    public static async Task<RegistrationResult> RegisterAsync(IServiceProvider services, string loginId, string password,
        CancellationToken cancellationToken = default)
    {
        var prepared = await services.GetRequiredService<ILauncherCredentialReader>()
            .PrepareRegistrationAsync(loginId, password, cancellationToken);
        if (prepared.Prepared is null) return new(AuthenticationStatus.InvalidRequest, FieldErrors: prepared.FieldErrors);
        var reply = await SendAsync(services.GetRequiredService<ICommandBus>(), HttpRouteOperation.Register,
            new HttpRouteInput.Registration(prepared.Prepared), cancellationToken);
        return Result<RegistrationResult>(reply);
    }

    /// <summary>Redoes the check once when the record changed before the command, as the login endpoint does.</summary>
    public static async Task<AuthenticationResult> LoginAsync(IServiceProvider services, string loginId, string password,
        CancellationToken cancellationToken = default)
    {
        var reader = services.GetRequiredService<ILauncherCredentialReader>();
        var commands = services.GetRequiredService<ICommandBus>();
        for (var attempt = 0; ; attempt++)
        {
            var verified = await reader.VerifyLoginAsync(loginId, password, cancellationToken);
            if (verified.Login is null) return new(verified.Status, FieldErrors: verified.FieldErrors);
            var result = Result<AuthenticationResult>(await SendAsync(commands, HttpRouteOperation.Login, new HttpRouteInput.Authentication(verified.Login), cancellationToken));
            if (result.Status != AuthenticationStatus.CredentialChanged) return result;
            if (attempt == 1) return new(AuthenticationStatus.InvalidCredentials);
        }
    }
    /// <summary>Persistence-only fixture setup; this is a real domain transaction, not HTTP/CMD entry evidence.</summary>
    internal static async Task<RegistrationResult> RegisterDomainAsync(IServiceProvider services, string loginId, string password)
    {
        var prepared = await services.GetRequiredService<ILauncherCredentialReader>().PrepareRegistrationAsync(loginId, password, default);
        if (prepared.Prepared is null) return new(AuthenticationStatus.InvalidRequest, FieldErrors: prepared.FieldErrors);
        return await DomainAsync(services, domain => domain.RegisterAsync(prepared.Prepared, default));
    }
    internal static async Task<AuthenticationResult> LoginDomainAsync(IServiceProvider services, string loginId, string password)
    {
        var reader = services.GetRequiredService<ILauncherCredentialReader>();
        for (var attempt = 0; ; attempt++)
        {
            var verified = await reader.VerifyLoginAsync(loginId, password, default);
            if (verified.Login is null) return new(verified.Status, FieldErrors: verified.FieldErrors);
            var result = await AuthenticateDomainAsync(services, verified.Login);
            if (result.Status != AuthenticationStatus.CredentialChanged) return result;
            if (attempt == 1) return new(AuthenticationStatus.InvalidCredentials);
        }
    }
    internal static Task<AuthenticationResult> AuthenticateDomainAsync(IServiceProvider services, VerifiedLogin login) =>
        DomainAsync(services, domain => domain.AuthenticateAndIssueTicketAsync(login, default));
    private static Task<T> DomainAsync<T>(IServiceProvider services, Func<IAuthenticationDomainService, ValueTask<T>> action) => TestGameplay.RunAsync(services, async () =>
    {
        await using var transaction = services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>().Begin();
        try
        {
            var result = await action(services.GetRequiredService<IAuthenticationDomainService>());
            await transaction.CommitAsync(default);
            return result;
        }
        catch (Exception error) { await transaction.AbortAsync(error); throw; }
    });

    internal static ValueTask<HttpRouteReply> SendAsync(ICommandBus commands, HttpRouteOperation operation, HttpRouteInput input, CancellationToken ct = default) =>
        commands.SendInternalAsync(new HttpRouteCommand(operation, input), new(GameplayCallSource.Http,
            GameplayCallChain.NewRoot(GameplayCallSource.Http), operation == HttpRouteOperation.Register
                ? "http:POST /api/launcher/v1/register" : "http:POST /api/launcher/v1/login", InternalCommandCancellation.BodyOnly), ct);
    internal static T Result<T>(HttpRouteReply reply)
    {
        if (reply.Error is { } error) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        return (T)reply.Value!;
    }

    /// <summary>The lightweight socket fixture is already running when credential setup is invoked.</summary>
    internal sealed class FixtureReadiness : Dfo.Server.Abstractions.Hosting.IGameServerReadiness
    { public bool NetworkEnabled => true; public bool IsReady => true; }

}
