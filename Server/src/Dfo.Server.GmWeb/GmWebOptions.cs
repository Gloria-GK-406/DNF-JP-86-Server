using Microsoft.Extensions.Configuration;

namespace Dfo.Server.GmWeb;

/// <param name="IconPack">Absolute path of the item icon zip (<c>GmWeb:IconPack</c>, relative paths resolve against the executable directory).</param>
public sealed record GmWebOptions(bool Enabled, Uri? ListenUrl, string IconPack = "")
{
    internal const string DefaultIconPack = "Data/gm-icons.zip";
    public static GmWebOptions FromConfiguration(IConfiguration configuration)
    {
        var raw = configuration["GmWeb:Enabled"];
        if (raw is not null && !bool.TryParse(raw, out _))
            throw new InvalidOperationException("GmWeb:Enabled must be true or false.");
        if (!bool.TryParse(raw, out var enabled) || !enabled) return new(false, null);
        var icons = string.IsNullOrWhiteSpace(configuration["GmWeb:IconPack"]) ? DefaultIconPack : configuration["GmWeb:IconPack"]!;
        if (!Uri.TryCreate(configuration["GmWeb:ListenUrl"], UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.AbsolutePath != "/" || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.Port is < 1 or > 65535)
            throw new InvalidOperationException("GmWeb:ListenUrl must be an HTTP(S) listener URL without path or credentials.");
        return new(true, uri, Path.GetFullPath(Path.IsPathRooted(icons) ? icons : Path.Combine(AppContext.BaseDirectory, icons)));
    }
}
