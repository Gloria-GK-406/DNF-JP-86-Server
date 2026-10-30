using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DeleteCharacter;

internal static class DeleteCharacterPayloadCodec
{
    /// <summary>Layout: <c>u16 rosterIndex</c>, <c>i32 nameLength</c>, name bytes.</summary>
    /// <remarks>
    /// A captured request reads <c>06 00 | 04 00 00 00 | "00a6"</c>. Taking the index as one byte
    /// puts the length field one byte early, over <c>00 04 00 00</c>, and every deletion is refused
    /// as malformed.
    /// </remarks>
    public static DeleteCharacterRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length < sizeof(ushort))
        {
            return new DeleteCharacterRequest(metadata, 0, null);
        }

        var rosterIndex = BinaryPrimitives.ReadUInt16LittleEndian(body);
        return CharacterNameField.TryRead(body, offset: sizeof(ushort), out var name)
            ? new DeleteCharacterRequest(metadata, rosterIndex, name)
            : new DeleteCharacterRequest(metadata, rosterIndex, null);
    }

    /// <summary>
    /// Success is the <see cref="GameCmdEnvelope"/> acceptance, a <c>u8</c> flag and the request's
    /// own index echoed back as a <c>u16</c>; a refusal is the envelope alone.
    /// </summary>
    /// <remarks>
    /// The handler at <c>RVA 0x008C5C60</c> reads nothing until the envelope says the deletion
    /// succeeded. Its success path is <c>ReadU8</c>, an optional UI call when that byte is
    /// non-zero, then <c>ReadU16</c> -- the id it removes from the roster view, after which it
    /// shows string <c>0x2B0</c> and answers with CMD 0x0286. Its refusal path reads nothing at
    /// all and switches on the envelope's reason byte instead.
    ///
    /// This is the whole reason deletion appeared to do nothing. The reply used to open with a
    /// status byte of its own, so the receive layer read <c>0x00</c> as "failed", took the id's low
    /// byte as the reason code, found no case for it and returned -- no message, no removal, while
    /// the server had already deleted the row. The inverted-looking convention was an artifact of
    /// mistaking the envelope for a payload field; deletion follows the same rule as everything
    /// else.
    ///
    /// The <c>u16</c> is the request's index echoed back, not the character id. Sending the id
    /// removed the wrong row from the roster view: the client indexes its own 936-byte character
    /// vector with it (<c>0x0203ECC0</c> bounds-checks against that vector's element count), and it
    /// is the same index the request builder used to look up the confirmation name
    /// (<c>0x01D60B31</c> pushes <c>[esi+0x138]</c> straight into <c>0x020332A0</c>, which returns
    /// <c>begin + index * 0x3A8</c>). Request and reply share one index space, so echoing is
    /// correct whatever that space turns out to be numbered by.
    ///
    /// The middle <c>u8</c> is sent as zero, which takes the shorter of the two success paths. Its
    /// non-zero branch calls into UI scene 0 before the index is read; nothing yet says when the
    /// server is supposed to ask for that.
    ///
    /// Reason codes the handler acts on: <c>0x02</c>, <c>0x13</c>, <c>0x15</c>, <c>0x16</c>,
    /// <c>0x24</c>, <c>0x28</c>, <c>0x76</c>, <c>0x9C</c>. Anything else is ignored in silence.
    /// </remarks>
    public static byte[] Encode(CharacterDeletionStatus status, ushort rosterIndex)
    {
        if (status is not CharacterDeletionStatus.Deleted)
        {
            return GameCmdEnvelope.Refuse(status switch
            {
                CharacterDeletionStatus.NameMismatch => 0x15,
                _ => 0x02,
            });
        }

        var payload = new byte[1 + sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1), rosterIndex);
        return GameCmdEnvelope.Accept(payload);
    }
}

internal sealed class DeleteCharacterMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        DeleteCharacterPayloadCodec.Decode(metadata, body);
}

public sealed class DeleteCharacterHandler(ICommandBus commands)
    : ClientMessageHandler<DeleteCharacterRequest>, IProtocolCommandMessageHandler<GameDeleteCharacterCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        DeleteCharacterRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameDeleteCharacterCommand(context, message), cancellationToken).ConfigureAwait(false);
}

internal sealed class DeleteCharacterResponseEncoder
    : GameCmdMessageEncoder<DeleteCharacterResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        DeleteCharacterResponse message) =>
        OnePayload(message,
            DeleteCharacterPayloadCodec.Encode(message.Status, message.RosterIndex));
}
