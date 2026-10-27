using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Data.Authentication;

/// <remarks>
/// <see cref="CredentialChanged"/> never reaches a client: the login CMD returns it when the record it
/// was handed no longer matches the stored one, and the endpoint checks again (RQ-007's two phases).
/// </remarks>
public enum AuthenticationStatus { Success, InvalidRequest, AccountExists, InvalidCredentials, CredentialChanged }

public sealed record RegistrationResult(AuthenticationStatus Status, string? LoginId = null,
    IReadOnlyDictionary<string, string>? FieldErrors = null);

public sealed record AuthenticationResult(AuthenticationStatus Status, string? MId = null,
    string? Ticket = null, DateTimeOffset? ExpiresAtUtc = null,
    IReadOnlyDictionary<string, string>? FieldErrors = null)
{
    public override string ToString() => $"AuthenticationResult {{ Status = {Status} }}";
}

/// <summary>A valid registration whose password was hashed off the executor.</summary>
public sealed record PreparedRegistration(string LoginId, string PasswordRecord)
{
    public override string ToString() => "PreparedRegistration { }";
}

/// <summary>Either a prepared registration or the field errors that stopped it before hashing.</summary>
public sealed record RegistrationPreparation(PreparedRegistration? Prepared,
    IReadOnlyDictionary<string, string>? FieldErrors = null);

/// <summary>
/// A password checked off the executor against <see cref="VerifiedRecord"/>; the login CMD trusts it
/// only while that record is still the stored one. <see cref="ReplacementRecord"/> is the rehash or
/// legacy migration to store with the login.
/// </summary>
public sealed record VerifiedLogin(string LoginId, AccountId AccountId, string VerifiedRecord, string? ReplacementRecord)
{
    public override string ToString() => "VerifiedLogin { }";
}

/// <summary><see cref="Login"/> is set only for <see cref="AuthenticationStatus.Success"/>.</summary>
public sealed record LoginVerification(AuthenticationStatus Status, VerifiedLogin? Login = null,
    IReadOnlyDictionary<string, string>? FieldErrors = null);
