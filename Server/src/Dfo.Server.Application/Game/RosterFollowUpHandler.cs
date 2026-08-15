using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Game;

/// <summary>
/// Consumes the two-byte 0x02A8 report and logs it. Fire-and-forget: no response, no domain
/// change, no command bus. The client sends it immediately after receiving the character roster.
/// </summary>
public sealed class RosterFollowUpHandler(
    IOnlineUserQueryService onlineUsers,
    ILogger<RosterFollowUpHandler> logger) : ClientMessageHandler<RosterFollowUpRequest>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        RosterFollowUpRequest message,
        CancellationToken cancellationToken)
    {
        var attributed = onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            && onlineUser is not null;

        logger.LogInformation(
            "Roster follow-up {ConnectionId} {Attribution}: value={Value}",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId} onlineUser={onlineUser.OnlineUserId}" : "unattributed",
            message.Value == RosterFollowUpDescriptor.ObservedSentinel
                ? $"0x{message.Value:X4} (sentinel)"
                : $"0x{message.Value:X4}");

        return ValueTask.CompletedTask;
    }
}

internal static class RosterFollowUpDescriptor
{
    /// <summary>Mirrors the codec constant so the log can flag the only value observed so far
    /// without the Application layer depending on Networking.</summary>
    internal const ushort ObservedSentinel = 0xFFFF;
}
