using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

/// <summary>The topics one refresh service walks each round, in this order.</summary>
public sealed record SelectedSessionRefreshTopics(IReadOnlyList<SelectedSessionRefreshTopic> Topics)
{
    public static SelectedSessionRefreshTopics All { get; } = new(Enum.GetValues<SelectedSessionRefreshTopic>());
}

/// <summary>
/// Pushes what turned over to characters still in the world: daily counts at the reset, premium
/// periods, fatigue. One timer walks every topic; each topic is its own lane under its own label.
/// </summary>
/// <remarks>
/// The topics differ only in what they project, and the domain service already keeps one record of
/// what each session was sent. A timer per topic added nothing but a class and a registration to forget.
/// </remarks>
public sealed class SelectedSessionRefreshService(SelectedSessionRefreshTopics topics, ICommandBus commands,
    TimeProvider clock, ILogger<SelectedSessionRefreshService> logger) : TypedGameplayTimerSource(commands, logger)
{
    protected override bool SerializeRounds => true;
    protected override string SourceName => "timer:SelectedSessionRefreshService";
    public Task RefreshAsync(CancellationToken token) => RunAsync(token);

    internal static string Label(SelectedSessionRefreshTopic topic) => "timer:SelectedSessionRefreshService/" + topic switch
    {
        SelectedSessionRefreshTopic.Altar => "altar",
        SelectedSessionRefreshTopic.Dimension => "dimension",
        SelectedSessionRefreshTopic.GoldSilverHorn => "gold-silver-horn",
        SelectedSessionRefreshTopic.ReviveCoin => "revive-coin",
        SelectedSessionRefreshTopic.Mirror => "mirror",
        SelectedSessionRefreshTopic.Heras => "heras",
        SelectedSessionRefreshTopic.Tournament => "tournament",
        SelectedSessionRefreshTopic.ItemUsage => "item-usage",
        SelectedSessionRefreshTopic.Premium => "premium",
        SelectedSessionRefreshTopic.Fatigue => "fatigue",
        SelectedSessionRefreshTopic.LostLand => "lost-land",
    };

    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token)
    {
        foreach (var topic in topics.Topics)
        {
            var label = Label(topic);
            try
            {
                var scan = await SubmitAsync(new CaptureSelectedSessionRefreshCommand(topic), root, label, token).ConfigureAwait(false);
                foreach (var session in scan.Sessions)
                {
                    ICommand<bool> command = topic switch
                    {
                        SelectedSessionRefreshTopic.Premium => new RefreshPremiumSessionCommand(scan.Period, session),
                        SelectedSessionRefreshTopic.ItemUsage => new RefreshItemUsageSessionCommand(scan.Period, session),
                        SelectedSessionRefreshTopic.Fatigue => new RefreshFatigueSessionCommand(scan.Period, session),
                        _ => new RefreshQuotaSessionCommand(topic, scan.Period, session),
                    };
                    await ItemAsync(command, root, label + "/session", token).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (GameplayJob.EndedAJob(error))
            { logger.LogWarning("Session refresh {Topic} failed; the round continues with the next topic", topic); }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        try { while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false)) await HostedAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}

internal static class SelectedSessionRefreshRegistration
{
    internal static IServiceCollection AddSelectedSessionRefresh(this IServiceCollection services)
    {
        services.AddSingleton(SelectedSessionRefreshTopics.All);
        services.AddTypedGameplayTimer<SelectedSessionRefreshService>();
        return services;
    }
}
