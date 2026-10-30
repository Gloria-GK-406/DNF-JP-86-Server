using System.Buffers.Binary;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
namespace Dfo.Server.Protocol.Game.MarriageProposal;
public enum WeddingCommentKind { Post, Delete, Read }
public sealed record WeddingCommentRequest(GameMessageMetadata Metadata, WeddingCommentKind Kind, uint Key, uint Id, byte[] Text) : IClientNetworkMessage
{
    public ClientMessageType MessageType => Kind switch
    {
        WeddingCommentKind.Post => ClientMessageType.GameWeddingCommentPost,
        WeddingCommentKind.Delete => ClientMessageType.GameWeddingCommentDelete,
        WeddingCommentKind.Read => ClientMessageType.GameWeddingCommentRead,
        _ => throw new InvalidDataException("Unknown wedding comment request."),
    };
}
internal abstract class WeddingCommentDecoder(WeddingCommentKind type) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata m, ReadOnlySpan<byte> b)
    {
        if (type == WeddingCommentKind.Post)
        {
            if (b.Length < 8) throw new InvalidDataException("Truncated wedding comment.");
            var length = BinaryPrimitives.ReadUInt32LittleEndian(b[4..]);
            if (length > 20 || b.Length != 8 + length) throw new InvalidDataException("Invalid wedding comment length.");
            return new WeddingCommentRequest(m, type, BinaryPrimitives.ReadUInt32LittleEndian(b), 0, b[8..].ToArray());
        }
        if (b.Length != (type == WeddingCommentKind.Delete ? 23 : 19)) throw new InvalidDataException("Invalid wedding comment request.");
        // First fourteen bytes are uninitialized native stack data, not an identity.
        return new WeddingCommentRequest(m, type, BinaryPrimitives.ReadUInt32LittleEndian(b[14..]),
            type == WeddingCommentKind.Delete ? BinaryPrimitives.ReadUInt32LittleEndian(b[18..]) : 0, []);
    }
}
internal sealed class WeddingCommentPostDecoder() : WeddingCommentDecoder(WeddingCommentKind.Post);
internal sealed class WeddingCommentDeleteDecoder() : WeddingCommentDecoder(WeddingCommentKind.Delete);
internal sealed class WeddingCommentReadDecoder() : WeddingCommentDecoder(WeddingCommentKind.Read);
internal sealed record WeddingCommentAck(WeddingCommentKind Kind, uint Key, uint Id, MarriageGuestbookResult Result) : IGameCmdMessage
{
    public ServerMessageType MessageType => Kind switch
    {
        WeddingCommentKind.Post => ServerMessageType.GameWeddingCommentPost,
        WeddingCommentKind.Delete => ServerMessageType.GameWeddingCommentDelete,
        WeddingCommentKind.Read => ServerMessageType.GameWeddingCommentRead,
        _ => throw new InvalidDataException("Unknown wedding comment response."),
    };
}
internal sealed class WeddingCommentEncoder : GameCmdMessageEncoder<WeddingCommentAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingCommentAck m)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write((byte)1); w.Write(m.Result.Status);
        if (m.Result.Status == 0)
        {
            if (m.Kind == WeddingCommentKind.Delete) w.Write(m.Id);
            if (m.Kind == WeddingCommentKind.Read)
            {
                w.Write(0u); // Native reader ignores this field; semantic meaning remains unknown.
                w.Write(m.Result.Comments.Count);
                foreach (var item in m.Result.Comments)
                {
                    w.Write(item.Id); var name = new byte[30]; MarriageChurchPayload.CopyName(item.AuthorName, name);
                    w.Write(name); w.Write(item.SortKey); w.Write(item.Text.Length); w.Write(item.Text);
                }
            }
        }
        return OnePayload(m, stream.ToArray());
    }
}

internal sealed class WeddingCommentHandler(ICommandBus commands) : ClientMessageHandler<WeddingCommentRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameWeddingCommentCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WeddingCommentRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameWeddingCommentCommand(context, message), ct).ConfigureAwait(false);
}
