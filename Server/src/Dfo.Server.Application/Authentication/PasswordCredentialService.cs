using Dfo.Server.Abstractions.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Dfo.Server.Application.Authentication;

internal sealed class PasswordCredentialService : IPasswordCredentialRuleService
{
    internal const string Scheme = "dfo-identity-v3:";
    private readonly PasswordHasher<object> hasher;
    private readonly object subject = new();
    private readonly string dummy;

    public PasswordCredentialService() : this(220000) { }

    internal PasswordCredentialService(int iterations)
    {
        hasher = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = iterations,
        }));
        dummy = hasher.HashPassword(subject, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    }

    public string Hash(string password) => Scheme + hasher.HashPassword(subject, password);

    public PasswordCheck Verify(string record, string password)
    {
        if (!record.StartsWith(Scheme, StringComparison.Ordinal) || record.Length > 2048)
            return PasswordCheck.Failed;
        try
        {
            var payload = record[Scheme.Length..];
            var bytes = Convert.FromBase64String(payload);
            if (bytes.Length < 13 || bytes[0] != 1) return PasswordCheck.Failed;
            return hasher.VerifyHashedPassword(subject, payload, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Verified,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.RehashNeeded,
                _ => PasswordCheck.Failed,
            };
        }
        catch (FormatException) { return PasswordCheck.Failed; }
        catch (ArgumentException) { return PasswordCheck.Failed; }
    }

    public void VerifyDummy(string password) => _ = hasher.VerifyHashedPassword(subject, dummy, password);

    internal static bool VerifyLegacyMd5(string record, string password) =>
        record.Length == 32 && record.All(char.IsAsciiHexDigit)
        && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(record), MD5.HashData(Encoding.UTF8.GetBytes(password)));
}
