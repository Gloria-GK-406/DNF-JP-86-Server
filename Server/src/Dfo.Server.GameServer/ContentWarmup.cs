using System.Diagnostics;
using Dfo.Server.Abstractions.Content;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.GameServer;

/// <summary>
/// Runs every <see cref="IContentWarmup"/> during StartingAsync: before any listener starts and off the
/// gameplay executor (spec RQ-091, appendix B "启动预热").
/// </summary>
/// <remarks>
/// One broken item does not stop the server: until this warmup existed the item was loaded by the first
/// request that needed it, and that request -- not startup -- failed. The cache keeps that answer (see
/// <see cref="IContentWarmup"/>), so the warning here is the only visible difference.
/// </remarks>
internal static class ContentWarmup
{
    /// <summary>
    /// Production never sets it. In-process test hosts that do not exercise the warmup turn it off: with it
    /// off, first loads happen on the executor exactly as they did before the warmup existed.
    /// </summary>
    public const string EnabledKey = "ContentWarmup:Enabled";

    public static bool IsEnabled(IConfiguration? configuration)
    {
        var value = configuration?[EnabledKey];
        if (string.IsNullOrWhiteSpace(value)) return true;
        return bool.TryParse(value, out var enabled)
            ? enabled
            : throw new InvalidOperationException($"{EnabledKey} must be true or false, not '{value}'.");
    }

    public static async Task<int> RunAsync(bool enabled, Func<IEnumerable<IContentWarmup>> participants, ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            logger.LogInformation("Content warmup skipped by configuration ({Key}=false)", EnabledKey);
            return 0;
        }
        var total = Stopwatch.StartNew();
        var items = 0;
        var failed = 0;
        foreach (var participant in participants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            items++;
            if (ContentWarmupRegistration.IsReplaced(participant))
            {
                logger.LogWarning("Content warmup skipped: {Item} has another service registered in its place", participant.Name);
                continue;
            }
            var timer = Stopwatch.StartNew();
            try
            {
                var count = await participant.WarmAsync(cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Content warmup: {Item} {Count} in {ElapsedMs} ms", participant.Name, count, timer.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                failed++;
                logger.LogWarning(error, "Content warmup failed: {Item}", participant.Name);
            }
        }
        // Items counts every participant, failed and replaced ones included.
        logger.LogInformation("Content warmup finished: {Items} items, {Failed} failed, in {ElapsedMs} ms",
            items, failed, total.ElapsedMilliseconds);
        return items;
    }
}
