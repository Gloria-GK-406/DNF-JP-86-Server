using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildCreation;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildInfo;

public sealed record GuildInfoRequest(ClientMessageType MessageType, GameMessageMetadata Metadata, bool IsEmpty)
    : IClientNetworkMessage;

internal sealed record GuildInfoMessage(Guild Guild, bool ViewerIsLeader, int MemberCount) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildInfo;
}

internal sealed record GuildSetChannelReply(byte? Error = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildSetRecommendedChannel;
}

internal sealed class GuildInfoEncoder : GameNotiMessageEncoder<GuildInfoMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildInfoMessage message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var guild = message.Guild;
        WriteText(writer, guild.Name);
        // A21 01195A62 writes the flag read by the channel settings button at 00793B89.
        writer.Write(message.ViewerIsLeader);
        writer.Write((byte)0);
        writer.Write(checked((ushort)message.MemberCount));
        writer.Write(checked((byte)guild.Level));
        writer.Write(0u);
        writer.Write((byte)0);
        WriteText(writer, "");
        writer.Write(new byte[3]); // Optional features, including the guild base.
        writer.Write(checked((byte)guild.Level));
        writer.Write(0u);
        WriteText(writer, "");
        WriteText(writer, "");
        writer.Write((ushort)0);
        WriteText(writer, guild.Promotion);

        // Six permission/name records, followed by empty purchases, alliance and contribution state.
        writer.Write(new byte[6 * (sizeof(uint) + 24)]);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write((byte)0);
        writer.Write(checked((uint)guild.RecommendedChannel));
        writer.Write((byte)0);
        writer.Write(new byte[4 * sizeof(uint)]);
        // The client looks up the channel using the server group followed by the channel ID.
        // JP86's channel script advertises channels in server group 1.
        writer.Write(guild.RecommendedChannel > 0 ? 1u : 0u);
        writer.Write(checked((uint)guild.RecommendedChannel));
        writer.Write(0u);
        writer.Write(0u);
        return OnePayload(message, stream.ToArray());
    }

    private static void WriteText(BinaryWriter writer, string text)
    {
        var bytes = GuildCreationPayloadCodec.EncodeGuildText(text);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}

internal sealed class GuildSetChannelReplyEncoder : GameCmdMessageEncoder<GuildSetChannelReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildSetChannelReply message) =>
        OnePayload(message, message.Error is { } error ? GameCmdEnvelope.Refuse(error) : GameCmdEnvelope.Accept([]));
}

internal abstract class EmptyGuildInfoRequestDecoder(ClientMessageType type) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildInfoRequest(type, metadata, body.IsEmpty);
}

internal sealed class RefreshGuildInfoDecoder() : EmptyGuildInfoRequestDecoder(ClientMessageType.GameGuildRefreshInfo);
internal sealed class SetGuildChannelDecoder() : EmptyGuildInfoRequestDecoder(ClientMessageType.GameGuildSetRecommendedChannel);

public sealed class GuildInfoRequestHandler(ICommandBus commands)
    : ClientMessageHandler<GuildInfoRequest>, IProtocolCommandMessageHandler<GameGuildInfoCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuildInfoRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildInfoCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildInfoRegistration
{
    internal static void AddGuildInfoSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<RefreshGuildInfoDecoder>(ClientMessageType.GameGuildRefreshInfo);
        services.AddGameCmdMessageDecoder<SetGuildChannelDecoder>(ClientMessageType.GameGuildSetRecommendedChannel);
        services.AddGameNotiMessageEncoder<GuildInfoMessage, GuildInfoEncoder>();
        services.AddGameCmdMessageEncoder<GuildSetChannelReply, GuildSetChannelReplyEncoder>();
        services.AddProtocolCommandHandler<GameGuildInfoCommand, bool, GameGuildInfoCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildRefreshInfo, typeof(GuildInfoRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildSetRecommendedChannel, typeof(GuildInfoRequest)));
        services.RegisterProtocolMessageHandler<GuildInfoRequestHandler, GuildInfoRequest, GameGuildInfoCommand>(ClientMessageType.GameGuildRefreshInfo);
        services.RegisterProtocolMessageHandler<GuildInfoRequestHandler, GuildInfoRequest, GameGuildInfoCommand>(ClientMessageType.GameGuildSetRecommendedChannel);
    }
}
