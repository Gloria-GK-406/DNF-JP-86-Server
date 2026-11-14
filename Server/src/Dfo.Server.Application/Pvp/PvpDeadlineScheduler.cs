using System.Threading.Channels;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Pvp;

/// <summary>
/// One kind of duel deadline. The source holds no deadlines: each round it scans the rooms through typed
/// internal commands, advances what is due, and reports the next deadline.
/// </summary>
public abstract class PvpDeadlineSource
{
    /// <summary>The label suffix the source's commands run under; unique among sources.</summary>
    public abstract string Name { get; }
    public abstract ValueTask<DateTimeOffset?> RunAsync(PvpDeadlineRound round, CancellationToken token);
}

/// <summary>A source's view of one scheduler round: typed submissions only, labelled under the source.</summary>
public sealed class PvpDeadlineRound
{
    private readonly PvpDeadlineScheduler owner;
    private readonly GameplayCallChain root;
    internal PvpDeadlineRound(PvpDeadlineScheduler owner, GameplayCallChain root, string label)
    { this.owner = owner; this.root = root; Label = label; }
    public string Label { get; }
    public ValueTask<T> SubmitAsync<T>(ICommand<T> command, CancellationToken token) where T : notnull =>
        owner.Submit(command, root, Label, token);
    /// <summary>One due item; its failure is isolated so the round continues with the next item.</summary>
    public ValueTask ItemAsync(ICommand<bool> command, string step, CancellationToken token) =>
        owner.Item(command, root, Label + "/" + step, token);
}

/// <summary>
/// The single scheduler for duel room deadlines, written as <c>DungeonDeadlineScheduler</c> is: pull the next
/// deadline from each source, and rescan every source when a committed room change wakes it.
/// </summary>
/// <remarks>Deadlines stay in the room owner; nothing here is retried or caught up.</remarks>
internal sealed class PvpDeadlineScheduler : TypedGameplayTimerSource, IPvpDeadlineWake
{
    // Floors the wait so an already-passed deadline cannot spin the loop.
    private static readonly TimeSpan MinimumWait = TimeSpan.FromMilliseconds(50);
    // A failed scan must not turn a live source into an infinite wait.
    private static readonly TimeSpan FailureRetry = TimeSpan.FromSeconds(1);

    private sealed class Lane(PvpDeadlineSource source)
    {
        public PvpDeadlineSource Source { get; } = source;
        public bool Scanned { get; set; }
        public DateTimeOffset? Next { get; set; }
    }

    private readonly Channel<bool> signal = System.Threading.Channels.Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly Lock gate = new();
    private bool woken;
    private readonly IReadOnlyList<Lane> lanes;
    private readonly TimeProvider clock;
    private readonly ILogger<PvpDeadlineScheduler> logger;

    public PvpDeadlineScheduler(IEnumerable<PvpDeadlineSource> sources, ICommandBus commands, TimeProvider clock,
        ILogger<PvpDeadlineScheduler> logger) : base(commands, logger)
    {
        this.clock = clock; this.logger = logger;
        lanes = sources.Select(source => new Lane(source)).ToArray();
        var duplicate = lanes.GroupBy(x => x.Source.Name).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw new InvalidOperationException($"Duplicate duel deadline source '{duplicate.Key}'.");
    }

    protected override string SourceName => "timer:PvpDeadlineScheduler";

    public void Wake()
    {
        lock (gate) woken = true;
        signal.Writer.TryWrite(true);
    }

    /// <summary>Test observation: whether a wake awaits the next round.</summary>
    internal bool IsWoken { get { lock (gate) return woken; } }

    internal ValueTask<T> Submit<T>(ICommand<T> command, GameplayCallChain root, string label, CancellationToken token) where T : notnull =>
        SubmitAsync(command, root, label, token);
    internal ValueTask Item(ICommand<bool> command, GameplayCallChain root, string label, CancellationToken token) =>
        ItemAsync(command, root, label, token);

    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token)
    {
        bool wake;
        lock (gate) { wake = woken; woken = false; }
        var now = clock.GetUtcNow();
        foreach (var lane in lanes)
        {
            // Every source scans once at start, then only when woken or due.
            if (!wake && lane.Scanned && !(lane.Next <= now)) continue;
            lane.Scanned = true;
            try { lane.Next = await lane.Source.RunAsync(new(this, root, SourceName + "/" + lane.Source.Name), token).ConfigureAwait(false); }
            catch (Exception error) when (GameplayJob.EndedAJob(error))
            {
                logger.LogWarning("Duel deadline source {Source} failed; rescanning after {Retry}", lane.Source.Name, FailureRetry);
                lane.Next = clock.GetUtcNow() + FailureRetry;
            }
        }
    }

    private DateTimeOffset? Earliest()
    {
        lock (gate) if (woken) return clock.GetUtcNow();
        if (lanes.Any(lane => !lane.Scanned)) return clock.GetUtcNow();
        DateTimeOffset? earliest = null;
        foreach (var lane in lanes)
            if (lane.Next is { } at && (earliest is null || at < earliest)) earliest = at;
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

public static class PvpDeadlineRegistration
{
    internal static IServiceCollection AddPvpDeadlineScheduler(this IServiceCollection services)
    {
        services.AddTypedGameplayTimer<PvpDeadlineScheduler>();
        services.TryAddSingleton<IPvpDeadlineWake>(provider => provider.GetRequiredService<PvpDeadlineScheduler>());
        return services;
    }

    public static IServiceCollection AddPvpDeadlineSource<T>(this IServiceCollection services) where T : PvpDeadlineSource
    {
        services.AddSingleton<PvpDeadlineSource, T>();
        return services;
    }
}
