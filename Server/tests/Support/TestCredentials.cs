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
        return await services.GetRequiredService<ICommandBus>()
            .SendAsync(new RegisterAccountCommand(prepared.Prepared), cancellationToken);
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
            var result = await commands.SendAsync(new AuthenticateAndIssueTicketCommand(verified.Login), cancellationToken);
            if (result.Status != AuthenticationStatus.CredentialChanged) return result;
            if (attempt == 1) return new(AuthenticationStatus.InvalidCredentials);
        }
    }
}
