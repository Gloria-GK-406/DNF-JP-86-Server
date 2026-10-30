using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Protocol.Game.Creatures;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MoveItemSpace;

/// <summary>
/// CMD 0x0013 <c>MOVE_ITEMSPACE</c>: an item changing slots.
/// </summary>
/// <remarks>
/// The one request behind every inventory drag, and behind the quick-slot bar as well -- the bar is
/// not its own container but slots 3 to 8 of the main list, so binding an item to a number key is
/// an ordinary move within one list.
///
/// Observed live on 2026-08-18. The client sent it unprompted, one frame after the tutorial reward
/// acknowledgement said the potion had landed in slot 65, asking for slot 3. Nothing answered, and
/// that frame is the last in the capture: the client holds the inventory closed until the
/// acknowledgement arrives, which is why every later drag looked like it did nothing at all.
/// </remarks>
public sealed record MoveItemSpaceRequest(
    GameMessageMetadata Metadata,
    GameSlot SourceSlot,
    int SourceInstanceValue,
    int MoveCount,
    GameSlot DestinationSlot,
    int DestinationInstanceValue) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameMoveItemSpace;
}

/// <summary>
/// The CMD acknowledgement for the same type: either where the item ended up, or why it did not
/// move.
/// </summary>
public sealed record MoveItemSpaceResponse(
    byte? ErrorCode,
    GameSlot SourceSlot,
    int MoveValue,
    GameSlot DestinationSlot) : IGameCmdMessage
{
    /// <summary>
    /// The client's own word for each refusal, read out of its string table rather than guessed.
    /// </summary>
    /// <remarks>
    /// The table is archived at
    /// <c>protocol/patterns/data/probes/cmd-reason-messages-20260818.md</c>. Of the fifteen codes
    /// this type draws, only three say anything true about moving an item; the rest belong to
    /// equipping, trading, or a restricted area, and borrowing one is handing the player a
    /// sentence about something that did not happen.
    /// <para>
    /// Code <c>4</c> says 「金库空间不足， 无法放入物品」 and is reserved for an automatic
    /// cargo deposit with no open empty slot. Missing sessions, bindings, items or lost races
    /// must not be reported as a space problem.
    /// </para>
    /// <para>
    /// Code <c>19</c> prints the same sentence as <c>23</c> and adds a log line about permissions,
    /// so <c>23</c> is the cleaner of the two. Code <c>18</c> writes to the log and draws nothing.
    /// Code <c>1</c> draws a placeholder in Chinese and an empty string in Japanese.
    /// </para>
    /// </remarks>
    public const byte NotMovable = 23;

    /// <summary>「物品栏已有同类物品」 -- the merge would burst the stack limit.</summary>
    public const byte StackLimitReached = 17;

    public const byte ContainerFull = 4;

    /// <summary>
    /// A code the client shows nothing for.
    /// </summary>
    /// <remarks>
    /// It appears in neither this type's branch chain nor the shared fallback table every handler
    /// consults, so the frame arrives and no text is drawn. Silence looks like a drag that did
    /// nothing, which is what a lost race is; the alternative is a sentence blaming the player for
    /// the server's own gap.
    /// </remarks>
    public const byte Silent = 2;

    public ServerMessageType MessageType => ServerMessageType.GameMoveItemSpace;

    public static MoveItemSpaceResponse Success(
        GameSlot source, int moveValue, GameSlot destination) =>
        new(null, source, moveValue, destination);

    public static MoveItemSpaceResponse Failure(
        GameSlot source, GameSlot destination, byte errorCode) =>
        new(errorCode, source, 0, destination);
}

internal static class MoveItemSpacePayloadCodec
{
    /// <summary>
    /// The shortest body the reference server will parse. Live frames are 24 bytes; the two fields
    /// past offset 14 are read only when the body is long enough to carry them, so a shorter one is
    /// still a valid request rather than a truncated frame.
    /// </summary>
    public const int MinimumBodyLength = 14;

    public static byte[] Encode(MoveItemSpaceResponse message)
    {
        if (message.ErrorCode is { } errorCode)
            return [0x00, errorCode, (byte)message.SourceSlot.Space,
                (byte)message.DestinationSlot.Space];

        var body = new byte[11];
        body[0] = 0x01;
        body[1] = (byte)message.SourceSlot.Space;
        BinaryPrimitives.WriteInt16LittleEndian(
            body.AsSpan(2, 2), checked((short)message.SourceSlot.Index));
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4, 4), message.MoveValue);
        body[8] = (byte)message.DestinationSlot.Space;
        BinaryPrimitives.WriteInt16LittleEndian(
            body.AsSpan(9, 2), checked((short)message.DestinationSlot.Index));
        return body;
    }
}

internal sealed class MoveItemSpaceDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < MoveItemSpacePayloadCodec.MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"MOVE_ITEMSPACE requires at least {MoveItemSpacePayloadCodec.MinimumBodyLength} "
                    + $"body bytes; received '{body.Length}'.");
        }

        return new MoveItemSpaceRequest(
            metadata,
            new GameSlot(
                (InventoryContainerType)body[0],
                BinaryPrimitives.ReadInt16LittleEndian(body[1..])),
            BinaryPrimitives.ReadInt32LittleEndian(body[3..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[7..]),
            new GameSlot(
                (InventoryContainerType)body[11],
                BinaryPrimitives.ReadInt16LittleEndian(body[12..])),
            body.Length >= 18 ? BinaryPrimitives.ReadInt32LittleEndian(body[14..]) : 0);
    }
}

internal sealed class MoveItemSpaceResponseEncoder : GameCmdMessageEncoder<MoveItemSpaceResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MoveItemSpaceResponse message) =>
        OnePayload(message, MoveItemSpacePayloadCodec.Encode(message));
}

public sealed class MoveItemSpaceHandler(ICommandBus commands)
    : ClientMessageHandler<MoveItemSpaceRequest>, IProtocolCommandMessageHandler<GameMoveItemSpaceCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        MoveItemSpaceRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameMoveItemSpaceCommand(context, message), cancellationToken).ConfigureAwait(false);
}
