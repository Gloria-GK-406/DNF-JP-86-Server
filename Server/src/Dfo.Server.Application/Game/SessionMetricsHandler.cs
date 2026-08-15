using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Game;

/// <summary>
/// Consumes the SESSION_METRICS (0x00FA) report and logs it. Fire-and-forget like the other
/// telemetry: no response, no domain change, no command bus.
/// </summary>
public sealed class SessionMetricsHandler(
    IOnlineUserQueryService onlineUsers,
    ILogger<SessionMetricsHandler> logger) : ClientMessageHandler<SessionMetricsReportRequest>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SessionMetricsReportRequest message,
        CancellationToken cancellationToken)
    {
        var attributed = onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            && onlineUser is not null;

        logger.LogInformation(
            "Session metrics {ConnectionId} {Attribution}: means={Means} section7={Section7} section8={Section8}",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId} onlineUser={onlineUser.OnlineUserId}" : "unattributed",
            SessionMetricsDescriptor.DescribeMeans(message.Means),
            SessionMetricsDescriptor.DescribeSection(message.Section7),
            SessionMetricsDescriptor.DescribeSection(message.Section8));

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Renders SESSION_METRICS for the log without asserting more than the protocol record does. No
/// tag has a confirmed meaning, so tags are reported as numbers.
/// </summary>
internal static class SessionMetricsDescriptor
{
    /// <summary>A mean of zero means the client had no samples, so it is reported as "-" rather
    /// than as a measurement of zero. When nothing was sampled at all, say so once instead of
    /// printing seven dashes.</summary>
    internal static string DescribeMeans(IReadOnlyList<SessionMetricMean> means)
    {
        if (means.All(mean => mean.Mean == 0))
        {
            return $"{means.Count}tags nothing-sampled";
        }

        return string.Join(",", means.Select(mean => $"{mean.Tag}:{(mean.Mean == 0 ? "-" : mean.Mean.ToString())}"));
    }

    internal static string DescribeSection(IReadOnlyList<SessionMetricRecord> records) =>
        records.Count == 0
            ? "empty"
            : string.Join(";", records.Select(record =>
                $"{record.Word0}/{record.Byte2}/{record.Word3}/{record.Word5}/{record.Word7}"));
}
