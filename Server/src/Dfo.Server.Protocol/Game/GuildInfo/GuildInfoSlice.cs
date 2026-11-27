using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildCreation;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildInfo;

public sealed record GuildInfoRequest(ClientMessageType MessageType, GameMessageMetadata Metadata, bool IsEmpty)
    : IClientNetworkMessage;

internal sealed record GuildInfoMessage(
    Guild Guild, bool ViewerIsLeader, int MemberCount, int AttendanceCount = 0,
    IReadOnlyList<GuildRankSetting>? Ranks = null, IReadOnlyList<GuildPurchasedContent>? Contents = null,
    GuildAlliancePeer? Ally = null, GuildContributionAmounts? GuildContribution = null,
    GuildContributionAmounts? PersonalContribution = null)
    : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildInfo;
}

internal sealed record GuildSetChannelReply(byte? Error = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildSetRecommendedChannel;
}

internal sealed record GuildHallInfoMessage(byte EntranceStage) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildHallInfo;
}

internal sealed class GuildHallInfoEncoder : GameNotiMessageEncoder<GuildHallInfoMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildHallInfoMessage message) =>
        OnePayload(message, new byte[] { message.EntranceStage });
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
        WriteText(writer, guild.Announcement);
        writer.Write(new byte[3]); // Optional features, including the guild base.
        writer.Write(guild.Mark);
        writer.Write(0u);
        WriteText(writer, "");
        WriteText(writer, "");
        writer.Write(checked((ushort)message.AttendanceCount));
        WriteText(writer, guild.Promotion);

        foreach (var rank in message.Ranks ?? GuildRankSettings.Defaults)
        {
            // The accompanying A21 compatibility plugin preserves Disband when the alliance entry is shown.
            writer.Write(rank.Rank == GuildRankSettings.Leader
                ? rank.PermissionMask | GuildRankSettings.TransferLeadershipPermission | GuildRankSettings.ViewAlliancePermission
                : rank.PermissionMask & ~GuildRankSettings.TransferLeadershipPermission);
            var name = GuildCreationPayloadCodec.EncodeGuildText(rank.Name);
            writer.Write(name);
            writer.Write(new byte[GuildRankSettings.MaximumNameBytes + 1 - name.Length]);
        }

        var contents = message.Contents ?? [];
        writer.Write(checked((byte)contents.Count));
        foreach (var content in contents)
        {
            var hasUses = content.Kind == GuildContentKind.PartyTeleport;
            writer.Write((byte)content.Kind);
            writer.Write(content.ExpiresAtUnixSeconds == 0
                ? (uint)int.MaxValue : checked((uint)content.ExpiresAtUnixSeconds));
            writer.Write(hasUses);
            writer.Write(checked((uint)(hasUses ? content.RemainingUses : content.Value)));
        }

        writer.Write(0u);
        writer.Write(message.Ally is null ? (byte)0 : (byte)1);
        if (message.Ally is { } ally)
        {
            writer.Write(ally.Id.Value);
            WriteText(writer, ally.Name);
            writer.Write(ally.Mark);
        }
        writer.Write(checked((uint)guild.GmPoints));
        writer.Write((byte)0);
        writer.Write(checked((uint)guild.RecommendedChannel));
        writer.Write((byte)0);
        writer.Write(message.GuildContribution?.Lifetime ?? 0u);
        writer.Write(message.GuildContribution?.Monthly ?? 0u);
        writer.Write(message.PersonalContribution?.Lifetime ?? 0u);
        writer.Write(message.PersonalContribution?.Monthly ?? 0u);
        // The client looks up the channel using the server group followed by the channel ID.
        // JP86's channel script advertises channels in server group 1.
        writer.Write(guild.RecommendedChannel > 0 ? 1u : 0u);
        writer.Write(checked((uint)guild.RecommendedChannel));
        writer.Write(guild.MedalAura.Symbol);
        writer.Write(guild.MedalAura.Frame);
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
        services.AddGameNotiMessageEncoder<GuildHallInfoMessage, GuildHallInfoEncoder>();
        services.AddNotificationHandler<GuildInfoPanel, GuildInfoPanelHandler>();
        services.AddGameCmdMessageEncoder<GuildSetChannelReply, GuildSetChannelReplyEncoder>();
        services.AddProtocolCommandHandler<GameGuildInfoCommand, bool, GameGuildInfoCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildRefreshInfo, typeof(GuildInfoRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildSetRecommendedChannel, typeof(GuildInfoRequest)));
        services.RegisterProtocolMessageHandler<GuildInfoRequestHandler, GuildInfoRequest, GameGuildInfoCommand>(ClientMessageType.GameGuildRefreshInfo);
        services.RegisterProtocolMessageHandler<GuildInfoRequestHandler, GuildInfoRequest, GameGuildInfoCommand>(ClientMessageType.GameGuildSetRecommendedChannel);
    }
}
