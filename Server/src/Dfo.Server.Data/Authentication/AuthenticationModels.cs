namespace Dfo.Server.Data.Authentication;

public enum AuthenticationStatus { Success, InvalidRequest, AccountExists, InvalidCredentials }

public sealed record RegistrationResult(AuthenticationStatus Status, string? LoginId = null,
    IReadOnlyDictionary<string, string>? FieldErrors = null);

public sealed record AuthenticationResult(AuthenticationStatus Status, string? LoginId = null,
    string? Ticket = null, DateTimeOffset? ExpiresAtUtc = null,
    IReadOnlyDictionary<string, string>? FieldErrors = null)
{
    public override string ToString() => $"AuthenticationResult {{ Status = {Status} }}";
}
