using Microsoft.Extensions.Configuration;

namespace Dfo.Server.GmWeb;

public sealed record GmWebOptions(bool Enabled, Uri? ListenUrl)
{
    public static GmWebOptions FromConfiguration(IConfiguration configuration)
    {
        var raw = configuration["GmWeb:Enabled"];
        if (raw is not null && !bool.TryParse(raw, out _))
            throw new InvalidOperationException("GmWeb:Enabled must be true or false.");
        if (!bool.TryParse(raw, out var enabled) || !enabled) return new(false, null);
        if (!Uri.TryCreate(configuration["GmWeb:ListenUrl"], UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.AbsolutePath != "/" || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.Port is < 1 or > 65535)
            throw new InvalidOperationException("GmWeb:ListenUrl must be an HTTP(S) listener URL without path or credentials.");
        return new(true, uri);
    }
}
