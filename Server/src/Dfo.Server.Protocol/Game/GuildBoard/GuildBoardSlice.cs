using System.Buffers.Binary;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildCreation;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildBoard;

public sealed record GuildBoardRequest(ClientMessageType MessageType, GameMessageMetadata Metadata, bool Valid,
    string? Text = null, uint PostId = 0) : IClientNetworkMessage;

internal sealed class GuildBoardOpenDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildBoardRequest(ClientMessageType.GameGuildBoardOpen, metadata, body is [0] or [1]);
}

internal sealed class GuildBoardWriteDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var text = GuildCreationPayloadCodec.DecodeText(body, 1, 360);
        return new GuildBoardRequest(ClientMessageType.GameGuildBoardWrite, metadata, text is not null, text);
    }
}

internal sealed class GuildBoardDeleteDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var postId = body.Length == sizeof(uint) ? BinaryPrimitives.ReadUInt32LittleEndian(body) : 0;
        return new GuildBoardRequest(ClientMessageType.GameGuildBoardDelete, metadata, postId is > 0 and <= int.MaxValue,
            PostId: postId);
    }
}

internal sealed record GuildBoardReply(ServerMessageType MessageType, GuildBoardSnapshot? Snapshot, byte Error = 0x22)
    : IGameCmdMessage;

internal sealed class GuildBoardEncoder : GameCmdMessageEncoder<GuildBoardReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildBoardReply message)
    {
        if (message.Snapshot is not { } result)
            return OnePayload(message, GameCmdEnvelope.Refuse(message.Error));

        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        var count = checked((byte)result.Posts.Count);
        writer.Write(count);
        writer.Write(count);
        foreach (var post in result.Posts)
        {
            writer.Write(post.AuthorIsLeader);
            writer.Write(post.AuthorJob);
            writer.Write(post.Author == result.Viewer);
            ChatDString.Write(writer, post.AuthorName);
            writer.Write(post.Id);
            writer.Write((uint)Math.Clamp((result.ReadAt - post.PostedAt).TotalSeconds, 0, uint.MaxValue));
            ChatDString.Write(writer, GuildCreationPayloadCodec.EncodeGuildText(post.Text));
        }
        return OnePayload(message, GameCmdEnvelope.Accept(body.ToArray()));
    }
}

public sealed record GameGuildBoardCommand(NetworkDispatchContext Dispatch, GuildBoardRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameGuildBoardCommandHandler(ISelectedSessionDomainService sessions, IGuildBoardDomainService board,
    ICommandWorkUnits units, IGameCommandOutbox outbox) : ICommandHandler<GameGuildBoardCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameGuildBoardCommand command,
        CancellationToken ct)
    {
        var input = command.Input;
        if (GameRequestFreshness.IsStale(input.Metadata))
            return false;

        var connection = command.Dispatch.Connection;
        var reply = (ServerMessageType)input.MessageType;
        if (!input.Valid || !sessions.TryGetInWorld(connection.ConnectionId, out var actor))
        {
            if (input.MessageType != ClientMessageType.GameGuildBoardDelete)
                await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildBoardReply(reply, null), ct).ConfigureAwait(false);
            return false;
        }

        var declaration = new CommandWorkUnitDeclaration("guild-board", [new(actor, connection, input.Metadata)]);
        if (input.MessageType == ClientMessageType.GameGuildBoardOpen)
        {
            var result = await units.ReadOnlyAsync(execution, declaration, async token =>
                new GuildBoardReply(reply, await board.ReadAsync(actor.CharacterId, token).ConfigureAwait(false)), ct)
                .ConfigureAwait(false);
            await outbox.RegisterCmdAsync(connection.ConnectionId, result, ct).ConfigureAwait(false);
            return result.Snapshot is not null;
        }

        if (input.MessageType == ClientMessageType.GameGuildBoardDelete)
        {
            var deleted = await units.WriteAsync(execution, declaration,
                (_, token) => board.DeleteAsync(actor.CharacterId, input.PostId, token), ct).ConfigureAwait(false);
            if (!deleted)
                await outbox.RegisterNotiAsync(connection.ConnectionId,
                    new GameServerNoticeMessage("帖子已删除，或你没有删除权限。请刷新公会告示栏。"), ct).ConfigureAwait(false);
            return deleted;
        }

        var written = await units.WriteAsync(execution, declaration,
            (_, token) => board.WriteAsync(actor.CharacterId, input.Text ?? string.Empty, token), ct).ConfigureAwait(false);
        await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildBoardReply(reply, written.Snapshot), ct)
            .ConfigureAwait(false);
        if (!written.Written && written.Snapshot is not null)
            await outbox.RegisterNotiAsync(connection.ConnectionId,
                new GameServerNoticeMessage("未能发帖，请检查内容长度及告示栏容量。"), ct).ConfigureAwait(false);
        return written.Written;
    }
}

public sealed class GuildBoardHandler(ICommandBus commands)
    : ClientMessageHandler<GuildBoardRequest>, IProtocolCommandMessageHandler<GameGuildBoardCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuildBoardRequest message,
        CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildBoardCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildBoardRegistration
{
    internal static void AddGuildBoardSlice(IServiceCollection services)
    {
        const ClientMessageType open = ClientMessageType.GameGuildBoardOpen;
        const ClientMessageType write = ClientMessageType.GameGuildBoardWrite;
        const ClientMessageType delete = ClientMessageType.GameGuildBoardDelete;
        services.AddGameCmdMessageDecoder<GuildBoardOpenDecoder>(open);
        services.AddGameCmdMessageDecoder<GuildBoardWriteDecoder>(write);
        services.AddGameCmdMessageDecoder<GuildBoardDeleteDecoder>(delete);
        services.AddGameCmdMessageEncoder<GuildBoardReply, GuildBoardEncoder>();
        services.AddProtocolCommandHandler<GameGuildBoardCommand, bool, GameGuildBoardCommandHandler>(
            new ProtocolCommandMessageBinding(open, typeof(GuildBoardRequest)),
            new ProtocolCommandMessageBinding(write, typeof(GuildBoardRequest)),
            new ProtocolCommandMessageBinding(delete, typeof(GuildBoardRequest)));
        services.RegisterProtocolMessageHandler<GuildBoardHandler, GuildBoardRequest, GameGuildBoardCommand>(open);
        services.RegisterProtocolMessageHandler<GuildBoardHandler, GuildBoardRequest, GameGuildBoardCommand>(write);
        services.RegisterProtocolMessageHandler<GuildBoardHandler, GuildBoardRequest, GameGuildBoardCommand>(delete);
    }
}
