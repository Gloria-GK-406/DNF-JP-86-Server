using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.RosterFollowUp;

/// <summary>
/// Consumes the two-byte 0x02A8 report, logs it and answers it. No domain change, no command bus.
/// The client sends it immediately after receiving the character roster.
/// </summary>
/// <remarks>
/// It was treated as fire-and-forget until the reference server was driven through the same
/// sequence and diffed: it answers with two zero bytes. See <see cref="RosterFollowUpResponse"/>
/// for why the earlier "no response" reading was circular.
/// </remarks>
public sealed class RosterFollowUpHandler(
    IOnlineUserQueryDomainService onlineUsers,
    IGameMessageSender sender,
    ILogger<RosterFollowUpHandler> logger) : ClientMessageHandler<RosterFollowUpRequest>
{
    protected override async ValueTask HandleMessageAsync(
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

        // Not optional, and not merely "what the reference server happens to do". The client clears
        // a readiness flag on the roster screen immediately before sending this request and only
        // this reply sets it back; eighteen sites on that screen skip their work while it is clear.
        // Withholding the reply leaves the screen permanently inert -- which is exactly what an A/B
        // with this line removed produced. See protocol/game/client-to-server/0x02A8.md.
        await sender.SendCmdAsync(
            context.Connection.ConnectionId,
            new RosterFollowUpResponse(),
            cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class RosterFollowUpResponseEncoder : GameCmdMessageEncoder<RosterFollowUpResponse>
{
    /// <remarks>
    /// A refusal, deliberately, and the only reply in the family whose handler is inverted: at
    /// <c>RVA 0x008C6C60</c> it is <c>cmp byte [ebp+0x0C], 0 / jne return</c>, so the readiness call
    /// happens on the failure branch and an acceptance would return without doing anything. That is
    /// also why the reference server's <c>00 00</c> works -- it is not a two-byte acknowledgement,
    /// it is the envelope saying "failed, reason 0", and the reason is unused.
    /// </remarks>
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        RosterFollowUpResponse message) =>
        OnePayload(GameProtocol.RosterFollowUpType, GameCmdEnvelope.Refuse(0x00));
}

internal static class RosterFollowUpDescriptor
{
    /// <summary>Mirrors <see cref="RosterFollowUpPayloadCodec.ObservedSentinel"/> so the log can
    /// flag the only value observed so far. The two now sit in one slice, so the copy no longer
    /// buys the layering separation it was introduced for.</summary>
    internal const ushort ObservedSentinel = 0xFFFF;
}
