using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

/// <summary>Plan amendment A1: the game server's startup runs the duel verification and fails with it.</summary>
public sealed class PvpStartupVerificationHostTests
{
    [PvfFact]
    public async Task Duel_content_that_does_not_load_fails_startup()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => GmTestHost.StartAsync(
            configureServices: services => services.AddSingleton<IPvpGradeDefinitionSource, BrokenGrades>()));

        Assert.Contains(BrokenGrades.Message, error.ToString(), StringComparison.Ordinal);
    }

    [PvfFact]
    public async Task A_host_whose_duel_channels_and_content_check_out_starts()
    {
        await using var host = await GmTestHost.StartAsync();

        Assert.Contains(host.Logs.Messages, message => message.StartsWith("Duel channels verified:", StringComparison.Ordinal));
    }

    private sealed class BrokenGrades : IPvpGradeDefinitionSource
    {
        public const string Message = "pvp grade table withheld by the startup test";

        public PvpGradeCatalog Load() => throw new InvalidDataException(Message);
    }
}
