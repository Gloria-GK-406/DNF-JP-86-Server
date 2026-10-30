using System.Threading.Channels;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

/// <summary>
/// One kind of deadline inside a dungeon run. The source holds no deadlines: each round it scans the
/// owning domain through typed internal commands, advances what is due, and reports the next deadline.
/// </summary>
public abstract class DungeonDeadlineSource
{
    public abstract DungeonDeadlineKind Kind { get; }
    /// <summary>Rescan period while the source has no deadline; null waits for a wake.</summary>
    public virtual TimeSpan? IdlePoll => null;
    public abstract ValueTask<DateTimeOffset?> RunAsync(DungeonDeadlineRound round, CancellationToken token);
}

/// <summary>A source's view of one scheduler round: typed submissions only, labelled under the source.</summary>
public sealed class DungeonDeadlineRound
{
    private readonly DungeonDeadlineScheduler owner;
    private readonly GameplayCallChain root;
    internal DungeonDeadlineRound(DungeonDeadlineScheduler owner, GameplayCallChain root, string label)
    { this.owner = owner; this.root = root; Label = label; }
    public string Label { get; }
    public ValueTask<T> SubmitAsync<T>(ICommand<T> command, CancellationToken token) where T : notnull =>
        owner.Submit(command, root, Label, token);
    public ValueTask<T> SubmitAsync<T>(ICommand<T> command, string step, CancellationToken token) where T : notnull =>
        owner.Submit(command, root, Label + "/" + step, token);
    /// <summary>One due item; its failure is isolated so the round continues with the next item.</summary>
    public ValueTask ItemAsync(ICommand<bool> command, string step, CancellationToken token) =>
        owner.Item(command, root, Label + "/" + step, token);
}

/// <summary>
/// The single scheduler for deadlines that live inside a dungeon run. Not the system scheduled-task host:
/// deadlines appear and vanish with runs, are never retried or caught up, and move whenever play changes them.
/// </summary>
internal sealed class DungeonDeadlineScheduler : TypedGameplayTimerSource, IDungeonDeadlineWake
{
    // Floors the wait so an already-passed deadline cannot spin the loop.
    private static readonly TimeSpan MinimumWait = TimeSpan.FromMilliseconds(50);
    // A failed scan must not turn a live source into an infinite wait.
    private static readonly TimeSpan FailureRetry = TimeSpan.FromSeconds(1);

    private sealed class Lane(DungeonDeadlineSource source)
    {
        public DungeonDeadlineSource Source { get; } = source;
        public bool Scanned { get; set; }
        public DateTimeOffset? Next { get; set; }
        public DateTimeOffset LastRun { get; set; }
    }

    private readonly Channel<bool> signal = System.Threading.Channels.Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly Lock gate = new();
    private readonly HashSet<DungeonDeadlineKind> woken = [];
    private readonly IReadOnlyList<Lane> lanes;
    private readonly TimeProvider clock;
    private readonly ILogger<DungeonDeadlineScheduler> logger;

    public DungeonDeadlineScheduler(IEnumerable<DungeonDeadlineSource> sources, ICommandBus commands, TimeProvider clock,
        ILogger<DungeonDeadlineScheduler> logger) : base(commands, logger)
    {
        this.clock = clock; this.logger = logger;
        lanes = sources.Select(source => new Lane(source)).ToArray();
        var duplicate = lanes.GroupBy(x => x.Source.Kind).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw new InvalidOperationException($"Duplicate dungeon deadline source '{duplicate.Key}'.");
    }

    protected override string SourceName => "timer:DungeonDeadlineScheduler";

    public void Wake(DungeonDeadlineKind kind)
    {
        lock (gate) woken.Add(kind);
        signal.Writer.TryWrite(true);
    }

    /// <summary>Test observation: whether a wake of this kind awaits the next round.</summary>
    internal bool IsWoken(DungeonDeadlineKind kind) { lock (gate) return woken.Contains(kind); }

    internal ValueTask<T> Submit<T>(ICommand<T> command, GameplayCallChain root, string label, CancellationToken token) where T : notnull =>
        SubmitAsync(command, root, label, token);
    internal ValueTask Item(ICommand<bool> command, GameplayCallChain root, string label, CancellationToken token) =>
        ItemAsync(command, root, label, token);

    internal static string Label(DungeonDeadlineKind kind) => kind switch
    {
        DungeonDeadlineKind.RoomMechanism => "timer:DungeonDeadlineScheduler/room-mechanism",
        DungeonDeadlineKind.Altar => "timer:DungeonDeadlineScheduler/altar",
        DungeonDeadlineKind.GuardianAltar => "timer:DungeonDeadlineScheduler/guardian-altar",
    };

    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        foreach (var lane in lanes)
        {
            bool wake;
            lock (gate) wake = woken.Remove(lane.Source.Kind);
            // Every source scans once at start, then only when woken or due.
            if (!wake && lane.Scanned && !(Due(lane) <= now)) continue;
            lane.Scanned = true;
            lane.LastRun = now;
            try { lane.Next = await lane.Source.RunAsync(new(this, root, Label(lane.Source.Kind)), token).ConfigureAwait(false); }
            catch (Exception error) when (GameplayJob.EndedAJob(error))
            {
                logger.LogWarning("Dungeon deadline source {Kind} failed; rescanning after {Retry}", lane.Source.Kind, FailureRetry);
                lane.Next = clock.GetUtcNow() + FailureRetry;
            }
        }
    }

    private static DateTimeOffset? Due(Lane lane) =>
        lane.Next ?? (lane.Source.IdlePoll is { } poll ? lane.LastRun + poll : null);

    private DateTimeOffset? Earliest()
    {
        DateTimeOffset? earliest = null;
        lock (gate) if (woken.Count > 0) return clock.GetUtcNow();
        if (lanes.Any(lane => !lane.Scanned)) return clock.GetUtcNow();
        foreach (var lane in lanes)
            if (Due(lane) is { } at && (earliest is null || at < earliest)) earliest = at;
        return earliest;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await HostedAsync(stoppingToken).ConfigureAwait(false);
                await WaitAsync(Earliest(), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async ValueTask WaitAsync(DateTimeOffset? deadline, CancellationToken token)
    {
        if (deadline is null) { _ = await signal.Reader.ReadAsync(token).ConfigureAwait(false); return; }
        var delay = deadline.Value - clock.GetUtcNow();
        if (delay < MinimumWait) delay = MinimumWait;
        using var timer = new CancellationTokenSource(delay, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timer.Token);
        try { _ = await signal.Reader.ReadAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && timer.IsCancellationRequested) { }
    }
}

public static class DungeonDeadlineRegistration
{
    internal static IServiceCollection AddDungeonDeadlineScheduler(this IServiceCollection services)
    {
        services.AddTypedGameplayTimer<DungeonDeadlineScheduler>();
        services.TryAddSingleton<IDungeonDeadlineWake>(provider => provider.GetRequiredService<DungeonDeadlineScheduler>());
        return services;
    }

    public static IServiceCollection AddDungeonDeadlineSource<T>(this IServiceCollection services) where T : DungeonDeadlineSource
    {
        services.AddSingleton<DungeonDeadlineSource, T>();
        return services;
    }
}
