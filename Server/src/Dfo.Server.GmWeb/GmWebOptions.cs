using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.GmWeb;

/// <param name="IconPack">Absolute path of the item icon zip (<c>GmWeb:IconPack</c>, relative paths resolve against the executable directory).</param>
public sealed record GmWebOptions(bool Enabled, Uri? ListenUrl, string IconPack = "")
{
    internal const string DefaultIconPack = "Data/gm-icons.zip";
    internal const string TrustedProxiesKey = "GmWeb:TrustedProxies";

    /// <summary>
    /// Direct peers whose <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> the back office adopts
    /// (<c>GmWeb:TrustedProxies</c>). Empty means forwarding headers are ignored. IPv4-mapped IPv6
    /// entries are stored as IPv4.
    /// </summary>
    public IReadOnlyList<IPAddress> TrustedProxies { get; init; } = [];

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
        return new(true, uri, Path.GetFullPath(Path.IsPathRooted(icons) ? icons : Path.Combine(AppContext.BaseDirectory, icons)))
        {
            TrustedProxies = ReadTrustedProxies(configuration.GetSection(TrustedProxiesKey)),
        };
    }

    /// <summary>Array children plus a comma-separated scalar; every entry must be one canonical IP address.</summary>
    private static IReadOnlyList<IPAddress> ReadTrustedProxies(IConfigurationSection section)
    {
        var entries = section.GetChildren().Select(child => child.Value ?? "").ToList();
        // A JSON "[]" or an empty environment variable arrives as the value "": that is "no proxies".
        if (!string.IsNullOrWhiteSpace(section.Value)) entries.AddRange(section.Value.Split(','));
        var addresses = new List<IPAddress>();
        foreach (var entry in entries)
        {
            var address = ParseProxy(entry.Trim())
                ?? throw new InvalidOperationException(
                    $"{TrustedProxiesKey} entry '{entry}' must be a single IP address (no host name, CIDR range, port, scope or unspecified address).");
            if (!addresses.Contains(address)) addresses.Add(address);
        }
        return addresses;
    }

    private static IPAddress? ParseProxy(string text)
    {
        // Brackets would let "[::1]:80" through (the parser drops the port); '%' is a scope, '/' a range.
        if (text.Length == 0 || text.AsSpan().IndexOfAny("[]%/") >= 0 || !IPAddress.TryParse(text, out var address)) return null;
        if (address.AddressFamily == AddressFamily.InterNetwork && address.ToString() != text) return null;
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0) return null;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return null;
        return address;
    }
}
