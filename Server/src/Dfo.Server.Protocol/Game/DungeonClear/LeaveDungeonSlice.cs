using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game;
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
/// Ordinary state 1 options 0/1/2 exit. Option 3 is a separate confirmation: the native story
/// button first sends an exit option, then this confirmation. Once the source run is gone the
/// confirmation must not acknowledge another choice or rebuild the town a second time.
/// Explicit option 2 retains its no-run town-return compatibility. Non-exit states only ACK.
/// Specialized tower and altar paths retain their own settlement semantics.
/// </remarks>
public sealed class EplpCommandHandler(ICommandBus commands) : ClientMessageHandler<EplpCommandRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameEplpCommand>
{
    public const byte LeaveState = 1;
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, EplpCommandRequest message, CancellationToken token) =>
        _ = await commands.SendAsync(new GameEplpCommand(context, message), token).ConfigureAwait(false);
}
