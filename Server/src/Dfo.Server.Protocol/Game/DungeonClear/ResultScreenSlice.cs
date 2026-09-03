using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DungeonClear;

/// <summary>
/// CMD 0x0075 <c>BOSS_DIE_CHECK</c>: the client's own word that the boss is down.
/// </summary>
/// <remarks>
/// Thirty-nine bytes, of which this server reads the first four as two 16-bit numbers. That much
/// is measured: a live frame on 2026-08-29 opened <c>01 00 E9 0E</c>, and the run it belonged to
/// had reported actor 3817 -- <c>0x0EE9</c> -- dead moments earlier under wire user 1. What the
/// remaining thirty-five bytes are has not been established and nothing here reads them.
/// <para>
/// <b>Nothing is answered and nothing changes.</b> Both halves of that are decisions with
/// evidence behind them, written up in <c>protocol/game/client-to-server/0x0075.md</c>; the short
/// form is that the client registers no handler for a reply, and that the exit has already been
/// opened by the server's own accounting by the time this arrives.
/// </para>
/// </remarks>
public sealed record BossDieCheckRequest(
    GameMessageMetadata Metadata, ushort ReportedUserId, ushort ReportedActorSequence)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameBossDieCheck;
}

internal sealed class BossDieCheckDecoder : IGameMessageDecoder
{
    /// <summary>The two numbers this server reads, and no more of the body than that.</summary>
    public const int MinimumBodyLength = 4;

    /// <summary>
    /// Declines a body too short to carry those two numbers, rather than throwing.
    /// </summary>
    /// <remarks>
    /// The difference is the whole run. A decoder that throws is rethrown by
    /// <c>NetworkPlugins.Decode</c> and the connection drops with it -- which for a frame that
    /// arrives at settlement means the player loses the dungeon they just cleared. Declining drops
    /// this frame and nothing else, which is what the acceptance condition asks for, and it is
    /// also what this seam's own contract asks for: null means "not this message".
    /// <para>
    /// Nothing is lost by declining. This frame changes no state even when it parses.
    /// </para>
    /// </remarks>
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length < MinimumBodyLength
            ? null
            : new BossDieCheckRequest(
                metadata,
                BinaryPrimitives.ReadUInt16LittleEndian(body),
                BinaryPrimitives.ReadUInt16LittleEndian(body[2..]));
}

/// <summary>
/// Reads the client's boss-death confirmation, says whether it agrees, and answers nothing.
/// </summary>
/// <remarks>
/// <b>Why nothing is sent back.</b> The A21 client's dispatch tables register a response handler
/// for 671 command types. <c>0x0075</c> is not among them -- the census jumps from <c>0x0071</c>
/// to <c>0x0076</c> -- so a reply under this type would arrive at a client with nowhere to put it.
/// <para>
/// <b>Why nothing changes.</b> The reference server treats this frame as the trigger for clearing
/// the dungeon; this server does not, and deliberately. Here the exit opens when every hostile
/// actor the boss room declares has been reported dead, which has already happened by the time
/// this frame arrives -- the live ordering on 2026-08-29 was exit, then this. Adopting the
/// client's trigger would mean a run could be cleared by a client that merely says so, and would
/// replace a rule the server can check with one it cannot.
/// </para>
/// <para>
/// What is left is agreement, and it is worth logging rather than discarding: the actor the client
/// names should be one this run has already recorded dead in the room it is standing in. When it
/// is not, the two sides disagree about what died, and this line is the only place that would
/// ever say so.
/// </para>
/// </remarks>
public sealed class BossDieCheckHandler(
    ISelectedSessionDomainService selectedSessions,
    IDungeonRunDomainService runs,
    ILogger<BossDieCheckHandler> logger) : ClientMessageHandler<BossDieCheckRequest>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        BossDieCheckRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Boss die check {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return ValueTask.CompletedTask;
        }

        var run = runs.Find(session.CharacterId);
        if (run is null)
        {
            logger.LogWarning(
                "Boss die check {ConnectionId} character={CharacterId}: no run in progress, "
                    + "ignoring",
                context.Connection.ConnectionId,
                session.CharacterId.Value);
            return ValueTask.CompletedTask;
        }

        var agrees = run.DeadActors.Contains(
            new RoomActorId(run.CurrentCell, message.ReportedActorSequence));

        logger.LogInformation(
            "Boss die check {ConnectionId} character={CharacterId}: actor {Actor} of room {RoomId} "
                + "reported by user {User}; boss room={IsBossRoom} already dead here={Agrees}. "
                + "Nothing answered, nothing changed",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            message.ReportedActorSequence,
            run.CurrentRoomMapId,
            message.ReportedUserId,
            run.IsBossRoom,
            agrees);

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// CMD 0x0045 <c>SCORE_SCROLL_STATE</c>: the result scroll has finished drawing.
/// </summary>
/// <remarks>
/// An empty body, and the run stops here without an answer. The client's own reply handler is what
/// takes the run off the result screen -- it reads no field of the reply, only the fact of it --
/// and until 2026-08-29 this server had no such type at all, which is why a cleared dungeon left
/// the player looking at a finished scroll with no merchant and no way back to town.
/// </remarks>
public sealed record ScoreScrollStateRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameScoreScrollState;
}

/// <summary>The acknowledgement under the same type: the success byte every CMD result carries.</summary>
/// <remarks>
/// <b>The byte is required.</b> The client's receive path reads the first byte of a CMD result as
/// a success flag before it dispatches, so a zero or an empty body is not "a reply the handler
/// ignores" -- it is a failed result, and the frame never reaches the handler that moves the run
/// on. What the handler itself reads is nothing: there is no read primitive anywhere in its call
/// graph, so beyond that first byte the body's contents do not matter.
/// <para>
/// <b>The reference sends a second frame with it</b>, <c>CMD 0x0046</c>, carrying the card panel's
/// layout, <b>and so does this server now</b>. It used not to: a layout presupposes rewards to lay
/// out, and what a flip is worth was a domain this server did not have. It has it now -- the cards
/// of a clear are fixed at settlement, and <see cref="CardSelectRightStateResponse"/> is the frame
/// that puts them face down in front of the player. Without it the client closes the scroll and
/// offers no cards at all. See <c>protocol/game/client-to-server/0x0045.md</c>.
/// </para>
/// </remarks>
public sealed record ScoreScrollStateResponse : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameScoreScrollState;
}

internal sealed class ScoreScrollStateDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new ScoreScrollStateRequest(metadata);
}

internal sealed class ScoreScrollStateResponseEncoder
    : GameCmdMessageEncoder<ScoreScrollStateResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        ScoreScrollStateResponse message) =>
        OnePayload(GameProtocol.ScoreScrollStateType, new byte[] { 0x01 });
}

/// <summary>Answers, and puts the cards down behind the answer, whatever state the character is in.</summary>
/// <remarks>
/// Unconditionally, and unlike every other handler here, which returns early without a published
/// session. The scroll has already been drawn by the time this is sent and the client cannot ask
/// again, so a silence is indistinguishable from the server not having the type at all -- which is
/// the defect being fixed. There is nothing to withhold either: the answer carries no reward,
/// grants nothing and reads nothing, so a client that sent this outside a run learns nothing it
/// did not already have.
/// <para>
/// <b>Two frames, in that order.</b> The acknowledgement first, because the client's handler for it
/// is what moves the run off the result screen; the layout second, because that handler's own
/// branch is what would otherwise ask for it, and that branch cannot be decided without a live
/// client. Sending it unasked is what the reference server does and it costs nothing to be wrong
/// about: the layout carries no item, and a player with no cards fixed gets nothing for turning one
/// over.
/// </para>
/// </remarks>
public sealed class ScoreScrollStateHandler(
    ISelectedSessionDomainService selectedSessions,
    IGameMessageSender sender,
    ILogger<ScoreScrollStateHandler> logger) : ClientMessageHandler<ScoreScrollStateRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ScoreScrollStateRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);

        logger.LogInformation(
            "Score scroll state {ConnectionId} character={CharacterId}: acknowledged, "
                + "card layout sent",
            context.Connection.ConnectionId,
            session?.CharacterId.Value);

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [new ScoreScrollStateResponse(), new CardSelectRightStateResponse()], []),
            cancellationToken).ConfigureAwait(false);
    }
}
