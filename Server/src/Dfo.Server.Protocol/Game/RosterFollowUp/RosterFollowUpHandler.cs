using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.RosterFollowUp;

/// <summary>
/// Consumes the two-byte 0x02A8 report, logs it and answers it. No domain change.
/// The client sends it immediately after receiving the character roster.
/// </summary>
/// <remarks>
/// It was treated as fire-and-forget until the reference server was driven through the same
/// sequence and diffed: it answers with two zero bytes. See <see cref="RosterFollowUpResponse"/>
/// for why the earlier "no response" reading was circular.
/// </remarks>
public sealed class RosterFollowUpHandler(ICommandBus commands)
    : ClientMessageHandler<RosterFollowUpRequest>, IProtocolCommandMessageHandler<GameRosterFollowUpCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        RosterFollowUpRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameRosterFollowUpCommand(context, message), cancellationToken).ConfigureAwait(false);
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
