using System.IO;
using System.Net;

namespace Dfo.Launcher.Configuration;

public sealed record LauncherConfiguration(Uri BaseUrl);

public sealed class LauncherConfigurationException(string message) : Exception(message);

public static class LauncherConfigurationParser
{
    public static LauncherConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (LauncherConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new LauncherConfigurationException("无法读取 Launcher.ini，请检查文件和权限。");
        }
    }

    public static LauncherConfiguration Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sectionSeen = false;
        string? baseUrl = null;
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (sectionSeen || !string.Equals(line, "[Launcher]", StringComparison.Ordinal))
                {
                    throw Invalid();
                }

                sectionSeen = true;
                continue;
            }

            var separator = line.IndexOf('=');
            if (!sectionSeen || separator <= 0)
            {
                throw Invalid();
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!string.Equals(key, "BaseUrl", StringComparison.Ordinal)
                || baseUrl is not null
                || value.Length == 0)
            {
                throw Invalid();
            }

            baseUrl = value;
        }

        if (!sectionSeen || baseUrl is null
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
            || (uri.Scheme == Uri.UriSchemeHttp && !IsLoopback(uri)))
        {
            throw Invalid();
        }

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
                ? uri.AbsolutePath
                : uri.AbsolutePath + "/",
        };
        return new LauncherConfiguration(builder.Uri);
    }

    private static bool IsLoopback(Uri uri) =>
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));

    private static LauncherConfigurationException Invalid() =>
        new("Launcher.ini 必须只包含 [Launcher] 下的安全 BaseUrl。");
}
