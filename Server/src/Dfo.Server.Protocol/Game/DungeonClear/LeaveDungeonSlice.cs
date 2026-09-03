using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DungeonClear;

/// <summary>
/// CMD 0x0048 <c>EPLP_COMMAND</c>: the button that ends a run and walks the character out.
/// </summary>
/// <remarks>
/// Two bytes, a state and an option, and this is how a finished dungeon is actually left. Not
/// <c>GIVEUP_GAME</c>: a complete reference session captured on 2026-08-27 -- boss killed, result
/// screen, card flipped, character back in town -- contains no <c>0x002A</c> anywhere. It contains
/// this, twice, and the town state follows the second one.
/// <para>
/// This server had no such type until 2026-08-29, which is what a player meant by reporting that
/// returning to town from a dungeon did not work: the button reached a server that never answered,
/// so nothing moved and the client eventually rebuilt its whole session from the login handshake.
/// </para>
/// </remarks>
public sealed record EplpCommandRequest(GameMessageMetadata Metadata, byte State, byte Option)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameEplpCommand;
}

/// <summary>
/// The acknowledgement under the same type: a success byte and the two the request carried.
/// </summary>
/// <remarks>
/// The shape is the client's own, not the reference's. Its handler for this reply reads the
/// envelope's first byte as the success flag and then two more with <c>ReadU8</c>, and only takes
/// the branch that leaves the dungeon when the first of those two is 1 -- so the state has to come
/// back, and it has to come back as the client sent it. Echoing is not a convention here; a
/// different value in that byte is a different instruction.
/// </remarks>
public sealed record EplpCommandResponse(byte State, byte Option) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEplpCommand;
}

internal sealed class EplpCommandDecoder : IGameMessageDecoder
{
    /// <summary>A state and an option; the client's builder writes exactly these two.</summary>
    public const int BodyLength = 2;

    /// <summary>
    /// Declines a body too short to carry both, rather than throwing.
    /// </summary>
    /// <remarks>
    /// Throwing is rethrown by <c>NetworkPlugins.Decode</c> and takes the connection with it. On
    /// this frame that would be the worst possible moment: the player has pressed the button that
    /// leaves a finished dungeon, and the answer to a malformed press must not be to drop them.
    /// </remarks>
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length < BodyLength ? null : new EplpCommandRequest(metadata, body[0], body[1]);
}

internal sealed class EplpCommandResponseEncoder : GameCmdMessageEncoder<EplpCommandResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        EplpCommandResponse message) =>
        OnePayload(
            GameProtocol.EplpCommandType,
            new byte[] { 0x01, message.State, message.Option });
}

/// <summary>
/// Answers the button, and when it is the one that leaves, puts the character back in town.
/// </summary>
/// <remarks>
/// <b>Only state 1 leaves.</b> Both sides agree on that and the client is the one that settles it:
/// its reply handler compares the returned state against 1 and runs the leave path on nothing else.
/// The reference reads the same byte the same way. Every other state is answered and does nothing,
/// which is what the capture shows -- the session sent <c>02 02</c> first and <c>01 02</c> second,
/// and only the second was followed by the town state.
/// <para>
/// Answered whether or not there is a run, for the reason <c>BACK_2_VILLAGE</c> is: the client is
/// waiting on this frame with a button already pressed, and a silence is indistinguishable from
/// the server not having the type at all -- which is the defect being fixed. A character with no
/// run gets the acknowledgement and, if they asked to leave, the town they are already standing in.
/// </para>
/// </remarks>
public sealed class EplpCommandHandler(
    ISelectedSessionDomainService selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    IDungeonRunDomainService runs,
    IGameMessageSender sender,
    ILogger<EplpCommandHandler> logger) : ClientMessageHandler<EplpCommandRequest>
{
    /// <summary>The state that means "take me out of here".</summary>
    public const byte LeaveState = 1;

    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        EplpCommandRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null
            || !runtimeSessions.TryGet(session, out var result, out _)
            || result is null)
        {
            logger.LogWarning(
                "Eplp command {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return;
        }

        List<IGameServerMessage> frames =
            [new EplpCommandResponse(message.State, message.Option)];

        // Ended here rather than inside the log line: this is the state change, and a state change
        // hidden in an argument list is one that disappears the day somebody lowers the log level.
        var leaving = message.State == LeaveState;
        var runEnded = leaving && runs.End(session.CharacterId);
        if (leaving)
        {
            frames.AddRange(TownReturnProjection.Frames(session, result));
        }

        logger.LogInformation(
            "Eplp command {ConnectionId} character={CharacterId}: state={State} option={Option}; "
                + "leaving={Leaving} runEnded={RunEnded}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            message.State,
            message.Option,
            leaving,
            runEnded);

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(frames, []),
            cancellationToken).ConfigureAwait(false);
    }
}
