using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Dfo.Launcher.Api;

namespace Dfo.Launcher.Launching;

public sealed record GameLaunchResult(bool Succeeded, string? ErrorCode = null, string? Message = null);

public interface IGameLauncher
{
    GameLaunchResult Launch(LauncherLogin login);
}

public interface IGameProcessStarter
{
    void Start(ProcessStartInfo startInfo);
}

public interface ILauncherEventLog
{
    void Write(string eventCode);
}

public sealed class NullLauncherEventLog : ILauncherEventLog
{
    public static NullLauncherEventLog Instance { get; } = new();

    public void Write(string eventCode) { }
}

public static partial class A21LaunchArgumentBuilder
{
    private static readonly string[] Template =
    [
        "9", "", "", "", "", "1", "0", "6", "0", "2", "1", "", "", "0", "0",
        "0", "9n2b1c8r3w7y", "0", "0", "0", "0", "0", "2", "0", "0", "0", "0", "0",
        "0", "0", "0", "0", "0", "0", "4D87",
    ];

    public static string Build(LauncherLogin login)
    {
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(login.Channel);
        if (!IPAddress.TryParse(login.Channel.Host, out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || login.Channel.Port is < 1 or > 65535
            || !IsSafeLoginId(login.LoginId)
            || !TicketPattern().IsMatch(login.Ticket))
        {
            throw new ArgumentException("Login result cannot be encoded for A21.", nameof(login));
        }

        var slots = (string[])Template.Clone();
        slots[1] = login.Channel.Host;
        slots[2] = login.Channel.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        slots[3] = login.LoginId;
        slots[4] = login.Ticket;
        return string.Join('?', slots);
    }

    private static bool IsSafeLoginId(string value) =>
        !string.IsNullOrEmpty(value)
        && value.All(character => character is >= ' ' and <= '~' && character != '?');

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TicketPattern();
}

public sealed class GameLauncher(
    string launcherDirectory,
    TimeProvider clock,
    IGameProcessStarter processStarter,
    ILauncherEventLog log) : IGameLauncher
{
    public GameLaunchResult Launch(LauncherLogin login)
    {
        ArgumentNullException.ThrowIfNull(login);
        if (clock.GetUtcNow() >= login.ExpiresAtUtc)
        {
            log.Write("game_launch_ticket_expired");
            return new(false, "ticket_expired", "登录票据已过期，请重新登录。");
        }

        var executable = Path.Combine(launcherDirectory, "DNF.exe");
        if (!File.Exists(executable))
        {
            log.Write("game_launch_executable_missing");
            return new(false, "game_not_found", "启动器目录中未找到 DNF.exe。");
        }

        try
        {
            var argument = A21LaunchArgumentBuilder.Build(login);
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = launcherDirectory,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(argument);
            processStarter.Start(startInfo);
            log.Write("game_launch_succeeded");
            return new(true);
        }
        catch
        {
            log.Write("game_launch_failed");
            return new(false, "game_launch_failed", "无法启动游戏，请检查文件和权限后重试。");
        }
    }
}

public sealed class SystemGameProcessStarter : IGameProcessStarter
{
    public void Start(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Process creation returned no process.");
    }
}
