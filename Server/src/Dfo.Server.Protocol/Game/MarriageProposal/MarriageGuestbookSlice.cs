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
public sealed record WeddingCommentRequest(GameMessageMetadata Metadata, ushort Type, uint Key, uint Id, byte[] Text) : IClientNetworkMessage
{ public ClientMessageType MessageType => (ClientMessageType)(0x00010000 | Type); }
internal abstract class WeddingCommentDecoder(ushort type) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata m, ReadOnlySpan<byte> b)
    {
        if (type == 0x03F8)
        {
            if (b.Length < 8) throw new InvalidDataException("Truncated wedding comment.");
            var length = BinaryPrimitives.ReadUInt32LittleEndian(b[4..]);
            if (length > 20 || b.Length != 8 + length) throw new InvalidDataException("Invalid wedding comment length.");
            return new WeddingCommentRequest(m, type, BinaryPrimitives.ReadUInt32LittleEndian(b), 0, b[8..].ToArray());
        }
        if (b.Length != (type == 0x03F9 ? 23 : 19)) throw new InvalidDataException("Invalid wedding comment request.");
        // First fourteen bytes are uninitialized native stack data, not an identity.
        return new WeddingCommentRequest(m, type, BinaryPrimitives.ReadUInt32LittleEndian(b[14..]),
            type == 0x03F9 ? BinaryPrimitives.ReadUInt32LittleEndian(b[18..]) : 0, []);
    }
}
internal sealed class WeddingCommentPostDecoder() : WeddingCommentDecoder(0x03F8);
internal sealed class WeddingCommentDeleteDecoder() : WeddingCommentDecoder(0x03F9);
internal sealed class WeddingCommentReadDecoder() : WeddingCommentDecoder(0x03FA);
internal sealed record WeddingCommentAck(ushort Type, uint Key, uint Id, MarriageGuestbookResult Result) : IGameCmdMessage
{ public ServerMessageType MessageType => (ServerMessageType)(0x00010000 | Type); }
internal sealed class WeddingCommentEncoder : GameCmdMessageEncoder<WeddingCommentAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingCommentAck m)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write((byte)1); w.Write(m.Result.Status);
        if (m.Result.Status == 0)
        {
            if (m.Type == 0x03F9) w.Write(m.Id);
            if (m.Type == 0x03FA)
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
        var body = stream.ToArray();
        return m.Type switch { 0x03F8 => OnePayload(0x03F8, body), 0x03F9 => OnePayload(0x03F9, body),
            0x03FA => OnePayload(0x03FA, body), _ => throw new InvalidDataException("Unknown wedding comment response.") };
    }
}
public sealed record WeddingCommentCommand(SelectedSessionIdentity Actor, ushort Type, uint Key, uint Id, byte[] Text) : ICommand<MarriageGuestbookResult>, IAccountCommand, ISelectedSessionCommand
{ public AccountId AccountId => Actor.AccountId; public SelectedSessionIdentity? Session => Actor; }
public sealed class WeddingCommentCommandHandler(IMarriageVenueDomainService venue) : ICommandHandler<WeddingCommentCommand, MarriageGuestbookResult>
{
    public async ValueTask<MarriageGuestbookResult> HandleAsync(CommandExecutionContext c, WeddingCommentCommand q, CancellationToken ct) => q.Type switch
    {
        0x03FA => await venue.ReadGuestbookAsync(q.Actor, q.Key, ct).ConfigureAwait(false),
        0x03F8 => new(await venue.PostGuestbookAsync(q.Actor, q.Key, q.Text, ct).ConfigureAwait(false), []),
        0x03F9 => new(await venue.DeleteGuestbookAsync(q.Actor, q.Key, q.Id, ct).ConfigureAwait(false), []),
        _ => new(-1, [])
    };
}
internal sealed class WeddingCommentHandler(ISelectedSessionDomainService sessions, ICommandBus commands, IGameMessageSender sender) : ClientMessageHandler<WeddingCommentRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext c, WeddingCommentRequest m, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(c.Connection.ConnectionId);
        var result = actor is null ? new MarriageGuestbookResult(-1, []) : await commands.SendAsync(new WeddingCommentCommand(actor, m.Type, m.Key, m.Id, m.Text), ct).ConfigureAwait(false);
        await sender.SendExplicitAsync(c.Connection.ConnectionId, new([new WeddingCommentAck(m.Type, m.Key, m.Id, result)], []), ct).ConfigureAwait(false);
    }
}
