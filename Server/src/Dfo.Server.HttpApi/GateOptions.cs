using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.HttpApi;

/// <summary>
/// Gate settings read once at startup. A class rather than a record so no generated member prints
/// the token digest; the token itself is never kept.
/// </summary>
internal sealed class GateOptions
{
    internal const string TokenKey = "LauncherAuth:GateToken";
    internal const string AuthFailureLimitKey = "LauncherAuth:GateAuthFailureLimit";
    internal const string AuthFailureWindowKey = "LauncherAuth:GateAuthFailureWindowSeconds";
    internal const string LaunchKey = "LauncherAuth:Launch";
    internal const string AdminUrlKey = "LauncherAuth:AdminUrl";
    internal const int MinimumTokenLength = 32;

    private readonly byte[]? tokenSha256;

    private GateOptions(
        byte[]? tokenSha256,
        int authFailureLimit,
        TimeSpan authFailureWindow,
        string launch,
        string? adminUrl)
    {
        this.tokenSha256 = tokenSha256;
        AuthFailureLimit = authFailureLimit;
        AuthFailureWindow = authFailureWindow;
        Launch = launch;
        AdminUrl = adminUrl;
    }

    public bool TokenConfigured => tokenSha256 is not null;
    public int AuthFailureLimit { get; }
    public TimeSpan AuthFailureWindow { get; }
    public string Launch { get; }
    public string? AdminUrl { get; }

    public static GateOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var launch = configuration[LaunchKey]?.Trim();
        return new GateOptions(
            ReadTokenDigest(configuration[TokenKey]),
            ReadPositiveInteger(configuration, AuthFailureLimitKey, 10),
            TimeSpan.FromSeconds(ReadPositiveInteger(configuration, AuthFailureWindowKey, 900)),
            string.IsNullOrEmpty(launch) ? "a21" : launch,
            ReadAdminUrl(configuration[AdminUrlKey]));
    }

    /// <summary>
    /// The single gate token check. Both sides are reduced to SHA-256 first, so the fixed-time
    /// comparison always sees equal lengths and its duration does not depend on how close a guess is.
    /// </summary>
    public bool Matches(ReadOnlySpan<char> presented)
    {
        if (tokenSha256 is null) return false;

        var byteCount = Encoding.UTF8.GetByteCount(presented);
        var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var written = Encoding.UTF8.GetBytes(presented, buffer);
            Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(buffer.AsSpan(0, written), digest);
            return CryptographicOperations.FixedTimeEquals(digest, tokenSha256);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, byteCount));
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public override string ToString() =>
        $"GateOptions {{ TokenConfigured = {TokenConfigured}, AuthFailureLimit = {AuthFailureLimit}, "
        + $"AuthFailureWindow = {AuthFailureWindow}, Launch = {Launch}, AdminUrl = {AdminUrl} }}";

    private static byte[]? ReadTokenDigest(string? raw)
    {
        var token = raw?.Trim();
        if (string.IsNullOrEmpty(token)) return null;
        // Messages name the key and the rule only: the value is a credential.
        if (token.Length < MinimumTokenLength)
        {
            throw new InvalidOperationException(
                $"Launcher HTTP configuration '{TokenKey}' is invalid: too short (at least {MinimumTokenLength} characters).");
        }

        foreach (var character in token)
        {
            if (character is < '!' or > '~')
            {
                throw new InvalidOperationException(
                    $"Launcher HTTP configuration '{TokenKey}' is invalid: contains unsupported characters (printable ASCII without spaces only).");
            }
        }

        var bytes = Encoding.ASCII.GetBytes(token);
        try
        {
            return SHA256.HashData(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static int ReadPositiveInteger(IConfiguration configuration, string key, int fallback)
    {
        var raw = configuration[key]?.Trim();
        if (string.IsNullOrEmpty(raw)) return fallback;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            throw new InvalidOperationException(
                $"Launcher HTTP configuration '{key}' must be a positive integer.");
        }

        return value;
    }

    private static string? ReadAdminUrl(string? raw)
    {
        var url = raw?.Trim();
        if (string.IsNullOrEmpty(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Launcher HTTP configuration '{AdminUrlKey}' must be an absolute http or https URL.");
        }

        return url;
    }
}
