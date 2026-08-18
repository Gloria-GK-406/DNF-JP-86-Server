using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SaveGameOption;

/// <summary>
/// CMD 0x00C5 <c>SAVE_GAME_OPTION_1</c>: the client writing its main game-option blob back to the
/// server. Report-only -- the client registers no receive handler for this type, so answering it
/// would push a frame nothing dispatches.
/// </summary>
/// <remarks>
/// The blob is the same one the server pushed in NOTI 0x00AD, returned verbatim. The 2026-08-16
/// capture is a byte-for-byte round trip: the server sent <c>u32(134) + 134B + 8 zero bytes</c> and
/// the client sent back <c>u32(134) + 134B</c>, with those 134 bytes identical. The eight-byte tail
/// is ours alone and is not part of the blob.
///
/// See protocol/game/client-to-server/0x00C5.md and patterns/game-settings.md.
/// </remarks>
public sealed record SaveGameOption1Request(
    GameMessageMetadata Metadata,
    byte[] Options) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSaveGameOption1;
}

/// <summary>
/// Decodes <c>{i32 length, length bytes}</c>. The length is a real prefix rather than a fixed
/// width: the client's four option builders share this shape and each writes a different blob size
/// (134, 198, 300, 512), so a hard-coded 134 would be this protocol's size, not this family's.
/// </summary>
internal static class SaveGameOption1PayloadCodec
{
    public const int MinimumBodyLength = sizeof(int);

    public static SaveGameOption1Request Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length < MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"SAVE_GAME_OPTION_1 requires a four-byte length prefix; received '{body.Length}'.");
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(body);
        if (length < 0 || MinimumBodyLength + length > body.Length)
        {
            throw new InvalidDataException(
                $"SAVE_GAME_OPTION_1 declares '{length}' option bytes but the body holds "
                    + $"'{body.Length - MinimumBodyLength}'.");
        }

        return new SaveGameOption1Request(
            metadata,
            body.Slice(MinimumBodyLength, length).ToArray());
    }
}

internal sealed class SaveGameOption1MessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        SaveGameOption1PayloadCodec.Decode(metadata, body);
}

/// <summary>
/// Accepts the option blob and logs it. Nothing is persisted yet: the blob's internal layout is
/// undecoded, and storing an opaque buffer that the server then pushes back unchanged would look
/// like working settings while any server-side field the client expects to see change stayed
/// frozen. Recorded as an open item in the protocol document rather than half-implemented here.
/// </summary>
public sealed class SaveGameOption1Handler(
    IOnlineUserQueryDomainService onlineUsers,
    ILogger<SaveGameOption1Handler> logger) : ClientMessageHandler<SaveGameOption1Request>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SaveGameOption1Request message,
        CancellationToken cancellationToken)
    {
        var attributed = onlineUsers.TryGetByConnection(
            context.Connection.ConnectionId, out var onlineUser) && onlineUser is not null;

        logger.LogInformation(
            "Save game option 1 {ConnectionId} {Attribution}: {Length} option bytes, not persisted",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId}" : "unattributed",
            message.Options.Length);

        return ValueTask.CompletedTask;
    }
}
