using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.TaggedIdReport;

/// <summary>
/// Consumes the five-byte 0x008F report and logs it. Fire-and-forget: no response, no domain
/// change, no command bus.
///
/// The client sends this while handling the EXIT response, so it arrives after the server has
/// already acknowledged EXIT and the connection may already be closing. The handler therefore
/// tolerates an unattributed connection rather than treating it as an error.
/// </summary>
public sealed class TaggedIdReportHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ILogger<TaggedIdReportHandler> logger) : ClientMessageHandler<TaggedIdReportRequest>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        TaggedIdReportRequest message,
        CancellationToken cancellationToken)
    {
        var attributed = onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            && onlineUser is not null;

        logger.LogInformation(
            "Tagged id report {ConnectionId} {Attribution}: id={Id} flag={Flag}",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId} onlineUser={onlineUser.OnlineUserId}" : "unattributed",
            message.Id,
            message.Flag);

        return ValueTask.CompletedTask;
    }
}
