namespace Dfo.Server.Host.Tests;

/// <summary>
/// AC-007, shutdown on the real process (RQ-009): SIGTERM with a player connected reaches the
/// executor's stop after the teardown, and the process exits cleanly.
/// </summary>
public sealed class HostShutdownOrderTests
{
    [NonWindowsFact]
    public async Task Sigterm_with_an_open_game_connection_stops_the_executor_after_its_teardown_and_exits_cleanly()
    {
        await using var harness = await EndToEndHostHarness.StartAsync();

        var (exitCode, output) = await harness.StopGracefullyWithAnOpenGameConnectionAsync(TimeSpan.FromSeconds(20));

        Assert.True(exitCode == 0, $"exit code {exitCode?.ToString() ?? "none (still running)"}{Environment.NewLine}{output}");
        Assert.Contains("The gameplay executor stopped after draining its producers", output, StringComparison.Ordinal);
        Assert.DoesNotContain("did not run", output, StringComparison.Ordinal);
        Assert.DoesNotContain("did not drain within", output, StringComparison.Ordinal);
    }

    /// <summary>There is no way to send a child process SIGTERM on Windows.</summary>
    private sealed class NonWindowsFactAttribute : FactAttribute
    {
        public NonWindowsFactAttribute()
        {
            if (OperatingSystem.IsWindows()) Skip = "SIGTERM is not available on Windows.";
        }
    }
}
