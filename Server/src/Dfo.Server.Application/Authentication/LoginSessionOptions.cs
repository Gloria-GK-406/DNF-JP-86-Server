using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Application.Authentication;

internal sealed record LoginSessionOptions(int InitialLifetimeSeconds = 180, int ReconnectGraceSeconds = 300)
{
    public static LoginSessionOptions From(IConfiguration configuration) => new(
        Read(configuration, "LoginSession:InitialLifetimeSeconds", 180),
        Read(configuration, "LoginSession:ReconnectGraceSeconds", 300));

    private static int Read(IConfiguration configuration, string key, int fallback)
    {
        var raw = configuration[key];
        if (raw is null) return fallback;
        if (!int.TryParse(raw, out var value) || value <= 0)
            throw new InvalidOperationException($"{key} must be a positive number of seconds.");
        return value;
    }
}
