using System.Security.Cryptography;

namespace Dfo.Server.Testing;

/// <summary>
/// Valid gate tokens are generated per run (43 base64url characters); only deliberately broken
/// counterexamples are literals.
/// </summary>
internal static class GateTestToken
{
    public static string New() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
