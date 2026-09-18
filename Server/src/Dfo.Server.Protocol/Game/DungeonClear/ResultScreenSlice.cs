using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CardReward;
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
/// opened by the server's own Cell accounting by the time this arrives. A confirmed boss death
/// also retires the room's remaining monsters without paying their unearned kills.
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
/// the dungeon; this server does not, and deliberately. Here the exit opens from the recorded
/// Cell state: a confirmed boss death retires other live monsters in the same change. This has
/// already happened by the time this frame arrives -- the live ordering on 2026-08-29 was exit,
/// then this. Adopting the
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

        var agrees = run.CurrentCellState.FindActor(message.ReportedActorSequence)?.IsDead == true;

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
/// layout. With the presentation coordinator installed, the first timer or client request sends
/// this acknowledgement and the layout as one ordered delivery per participant.
/// <see cref="CardSelectRightStateResponse"/> puts the cards face down in front of the player.
/// See <c>protocol/game/client-to-server/0x0045.md</c>.
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

/// <summary>Opens the cards through the coordinator, or answers the scroll without it.</summary>
/// <remarks>
/// Without an active run, the scroll acknowledgement still carries no reward or state. During a
/// run, however, the timer may already have delivered the ordered pair before this client request
/// arrives, so answering here again would replay the presentation.
/// <para>
/// With the presentation coordinator installed, the first timer or client request sends the
/// acknowledgement and layout together. A later scroll request must not replay either frame.
/// The fallback without the coordinator sends the same pair directly.
/// </para>
/// </remarks>
public sealed class ScoreScrollStateHandler(
    ISelectedSessionDomainService selectedSessions,
    IGameMessageSender sender,
    ILogger<ScoreScrollStateHandler> logger,
    DungeonCardPresentationCoordinator? cardPresentation = null,
    IDungeonRunDomainService? runs = null)
    : ClientMessageHandler<ScoreScrollStateRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ScoreScrollStateRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);

        if (session is not null
            && runs?.Find(session.CharacterId) is { } run
            && cardPresentation is not null)
        {
            logger.LogInformation(
                "Score scroll state {ConnectionId} character={CharacterId}: "
                    + "routed through card presentation",
                context.Connection.ConnectionId,
                session.CharacterId.Value);
            await cardPresentation.OpenOnRequestAsync(
                session, run.RunId, cancellationToken).ConfigureAwait(false);
            return;
        }

        logger.LogInformation(
            "Score scroll state {ConnectionId} character={CharacterId}: acknowledged, "
                + "card layout sent here={LayoutSentHere}",
            context.Connection.ConnectionId,
            session?.CharacterId.Value,
            cardPresentation is null);

        IReadOnlyList<IGameServerMessage> frames = cardPresentation is null
            ? [new ScoreScrollStateResponse(), new CardSelectRightStateResponse()]
            : [new ScoreScrollStateResponse()];
        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(frames, []),
            cancellationToken).ConfigureAwait(false);
    }
}
