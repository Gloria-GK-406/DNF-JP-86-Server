using System.Diagnostics;
using Dfo.Launcher.Api;
using Dfo.Launcher.Launching;

namespace Dfo.Launcher.Tests.Launching;

public sealed class GameLauncherTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A21_argument_has_exactly_35_slots_and_the_contract_values()
    {
        var argument = A21LaunchArgumentBuilder.Build(Login("player", "ticket_123"));
        var slots = argument.Split('?');

        Assert.Equal(35, slots.Length);
        Assert.Equal("9", slots[0]);
        Assert.Equal("203.0.113.10", slots[1]);
        Assert.Equal("7001", slots[2]);
        Assert.Equal("player", slots[3]);
        Assert.Equal("ticket_123", slots[4]);
        Assert.Equal("9n2b1c8r3w7y", slots[16]);
        Assert.Equal("4D87", slots[34]);
        Assert.Equal(
            "9?203.0.113.10?7001?player?ticket_123?1?0?6?0?2?1???0?0?0?9n2b1c8r3w7y?0?0?0?0?0?2?0?0?0?0?0?0?0?0?0?0?0?4D87",
            argument);
    }

    [Fact]
    public void Safe_special_characters_in_login_id_do_not_split_the_single_argument()
    {
        var login = Login("player & name=\"quoted\"", "ticket-ABC_123");
        var argument = A21LaunchArgumentBuilder.Build(login);

        Assert.Equal(35, argument.Split('?').Length);
        Assert.Equal(login.LoginId, argument.Split('?')[3]);
    }

    [Theory]
    [InlineData("203.0.113.10", 0, "player", "ticket")]
    [InlineData("example.test", 7001, "player", "ticket")]
    [InlineData("203.0.113.10", 7001, "bad?player", "ticket")]
    [InlineData("203.0.113.10", 7001, "player", "bad ticket")]
    public void Unsafe_or_unrepresentable_values_are_rejected(
        string host,
        int port,
        string loginId,
        string ticket)
    {
        var login = new LauncherLogin(
            loginId,
            ticket,
            Now.AddMinutes(1),
            new LauncherChannelEndpoint(host, port));

        Assert.Throws<ArgumentException>(() => A21LaunchArgumentBuilder.Build(login));
    }

    [Fact]
    public void Launch_uses_same_directory_DNF_and_one_ArgumentList_entry_without_secrets()
    {
        var directory = CreateDirectoryWithGame();
        try
        {
            var starter = new RecordingProcessStarter();
            var log = new RecordingEventLog();
            var launcher = new GameLauncher(directory, new FixedTimeProvider(Now), starter, log);

            var result = launcher.Launch(Login("player", "ticket_123"));

            Assert.True(result.Succeeded);
            var startInfo = Assert.Single(starter.Starts);
            Assert.Equal(Path.Combine(directory, "DNF.exe"), startInfo.FileName);
            Assert.Equal(directory, startInfo.WorkingDirectory);
            Assert.False(startInfo.UseShellExecute);
            var argument = Assert.Single(startInfo.ArgumentList);
            Assert.Equal(35, argument.Split('?').Length);
            Assert.DoesNotContain("password", argument, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", argument, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(["game_launch_succeeded"], log.Events);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Ticket_is_expired_at_exact_server_expiry_and_never_starts_a_process()
    {
        var starter = new RecordingProcessStarter();
        var log = new RecordingEventLog();
        var launcher = new GameLauncher("unused", new FixedTimeProvider(Now), starter, log);

        var result = launcher.Launch(Login("player", "ticket", Now));

        Assert.False(result.Succeeded);
        Assert.Equal("ticket_expired", result.ErrorCode);
        Assert.Empty(starter.Starts);
        Assert.Equal(["game_launch_ticket_expired"], log.Events);
    }

    [Fact]
    public void Missing_executable_is_a_safe_recoverable_failure()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-launcher-game-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var starter = new RecordingProcessStarter();
            var log = new RecordingEventLog();
            var launcher = new GameLauncher(directory, new FixedTimeProvider(Now), starter, log);

            var result = launcher.Launch(Login("player", "ticket"));

            Assert.False(result.Succeeded);
            Assert.Equal("game_not_found", result.ErrorCode);
            Assert.Empty(starter.Starts);
            Assert.Equal(["game_launch_executable_missing"], log.Events);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Process_failure_is_sanitized_and_does_not_log_the_argument_or_ticket()
    {
        var directory = CreateDirectoryWithGame();
        try
        {
            var log = new RecordingEventLog();
            var launcher = new GameLauncher(
                directory,
                new FixedTimeProvider(Now),
                new ThrowingProcessStarter(),
                log);

            var result = launcher.Launch(Login("secret-login", "secret-ticket"));

            Assert.False(result.Succeeded);
            Assert.Equal("game_launch_failed", result.ErrorCode);
            Assert.DoesNotContain("secret", result.Message!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(["game_launch_failed"], log.Events);
            Assert.DoesNotContain("secret", string.Join('|', log.Events), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static LauncherLogin Login(
        string loginId,
        string ticket,
        DateTimeOffset? expiry = null) =>
        new(loginId, ticket, expiry ?? Now.AddMinutes(1), new("203.0.113.10", 7001));

    private static string CreateDirectoryWithGame()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-launcher-game-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "DNF.exe"), []);
        return directory;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingProcessStarter : IGameProcessStarter
    {
        public List<ProcessStartInfo> Starts { get; } = [];

        public void Start(ProcessStartInfo startInfo) => this.Starts.Add(startInfo);
    }

    private sealed class ThrowingProcessStarter : IGameProcessStarter
    {
        public void Start(ProcessStartInfo startInfo) =>
            throw new InvalidOperationException($"secret process error for {startInfo.FileName}");
    }

    private sealed class RecordingEventLog : ILauncherEventLog
    {
        public List<string> Events { get; } = [];

        public void Write(string eventCode) => this.Events.Add(eventCode);
    }
}
