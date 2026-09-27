using Microsoft.Extensions.Configuration;

namespace Dfo.Server.HttpApi;

public sealed record HttpApiOptions(bool Enabled, Uri? ListenUrl)
{
    public static HttpApiOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var rawEnabled = configuration["LauncherAuth:Enabled"];
        if (rawEnabled is not null && !bool.TryParse(rawEnabled, out _))
        {
            throw new InvalidOperationException(
                "Launcher HTTP configuration 'LauncherAuth:Enabled' must be true or false.");
        }

        var enabled = bool.TryParse(rawEnabled, out var parsed) && parsed;
        if (!enabled)
        {
            return new HttpApiOptions(false, null);
        }

        var raw = configuration["LauncherAuth:ListenUrl"];
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/"
            || uri.Port is < 1 or > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                "Required launcher HTTP configuration 'LauncherAuth:ListenUrl' is invalid.");
        }

        return new HttpApiOptions(true, uri);
    }
}
