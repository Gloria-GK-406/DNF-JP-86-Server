using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.Tests.Scheduling;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// The production composition (without listeners) on a temporary SQLite file and a manual clock,
/// with the gameplay queue recorded. Hosted services are only constructed; a test starts the one it
/// is about.
/// </summary>
internal sealed class TimerSourceHost : IAsyncDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "dfo-timer-source-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider provider;
    private readonly List<IHostedService> started = [];

    /// <param name="configure">Last registrations, after the modules.</param>
    /// <param name="wrapQueue">Wraps the container's queue inside the recording one.</param>
    public TimerSourceHost(Action<IServiceCollection>? configure = null, Func<IGameplayQueue, IGameplayQueue>? wrapQueue = null)
    {
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "channel_info.etc");
        File.WriteAllBytes(script, [1]);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Path"] = Path.Combine(directory, "timer.db"),
            ["Channel:ScriptPath"] = script,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        new InfrastructureModule().ConfigureServices(services, configuration);
        new PersistenceModule().ConfigureServices(services, configuration);
        new ApplicationModule().ConfigureServices(services, configuration);
        new Dfo.Server.Networking.NetworkingModule().ConfigureServices(services, configuration);
        new ProtocolModule().ConfigureServices(services, configuration);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IGameplayQueue>(sp =>
        {
            IGameplayQueue queue = new GameplayQueue(sp.GetRequiredService<GameplayShardSelector>());
            return new RecordingGameplayQueue(wrapQueue is null ? queue : wrapQueue(queue));
        });
        configure?.Invoke(services);
        provider = services.BuildServiceProvider();
        Queue = (RecordingGameplayQueue)provider.GetRequiredService<IGameplayQueue>();
    }

    public SchedulerClock Clock { get; } = new();

    public IServiceProvider Services => provider;

    public RecordingGameplayQueue Queue { get; }

    /// <summary>The registered hosted service whose type has this name; internal types are found this way.</summary>
    public IHostedService Hosted(string typeName) =>
        provider.GetServices<IHostedService>().Single(x => x.GetType().Name == typeName);

    public async Task StartAsync(IHostedService service)
    {
        started.Add(service);
        await service.StartAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var service in Enumerable.Reverse(started))
            await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await provider.DisposeAsync();
        SqliteTestFiles.Delete(directory, Path.Combine(directory, "timer.db"));
    }
}

internal static class SqliteTestFiles
{
    public static void Delete(string directory, string databasePath)
    {
        SqliteTestPools.ClearOwned(databasePath);
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
