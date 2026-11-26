using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game.GuildMemberInfo;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildRoster;

public sealed record GuildRosterRequest(ClientMessageType MessageType, GameMessageMetadata Metadata, bool Valid)
    : IClientNetworkMessage;

internal sealed record GuildRosterRow(GuildRosterEntry Entry, byte Channel);
internal sealed record GuildRosterSnapshot(GuildId Guild, byte[] LeaderName, IReadOnlyList<GuildRosterRow> Members);
internal sealed record GuildRosterReply(ServerMessageType MessageType, GuildRosterSnapshot? Roster = null) : IGameCmdMessage;

internal sealed class GuildMemberListDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildRosterRequest(ClientMessageType.GameGuildMemberList, metadata, body.IsEmpty);
}

internal sealed class GuildAllMemberListDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildRosterRequest(ClientMessageType.GameGuildAllMemberList, metadata, body is [0, 0, 0, 0]);
}

internal sealed class GuildRosterReplyEncoder : GameCmdMessageEncoder<GuildRosterReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildRosterReply message)
    {
        if (message.Roster is not { } roster || roster.Members.Count > ushort.MaxValue)
            return OnePayload(message, GameCmdEnvelope.Refuse(0));

        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        writer.Write(roster.Guild.Value);
        WriteText(writer, roster.LeaderName);
        writer.Write(0u); // Guild contribution is not part of the roster data yet.
        var count = (ushort)roster.Members.Count;
        writer.Write(count);
        writer.Write(count);
        var representedAccounts = new HashSet<AccountId>();
        foreach (var row in roster.Members)
        {
            var entry = row.Entry;
            writer.Write(entry.Membership.Character.Value);
            WriteText(writer, entry.Name);
            WriteText(writer, []);
            writer.Write(checked((ushort)entry.Level));
            writer.Write(checked((byte)entry.Job));
            writer.Write(checked((byte)entry.GrowType));
            writer.Write((byte)1); // Server group advertised by the channel script.
            writer.Write(row.Channel);
            writer.Write((byte)0); // Job label override.
            writer.Write((byte)0); // Visibility override.
            writer.Write((byte)GuildMemberInfoMessage.RankOf(entry.Membership));
            writer.Write(0u); // No persisted offline duration.
            writer.Write(entry.Account.Value);
            // A21 uses the first designated role as the header when the entire account is offline.
            writer.Write(representedAccounts.Add(entry.Account));
            WriteText(writer, []); // Account alias.
        }

        // The full-list reader consumes an extra UInt32 after the same member rows.
        if (message.MessageType == ServerMessageType.GameGuildAllMemberList)
            writer.Write(0u);

        return body.Length + GameServerFrameEncoder.HeaderLength + 1 <= DnfFrameProtocol.MaximumFrameLength
            ? OnePayload(message, GameCmdEnvelope.Accept(body.ToArray()))
            : OnePayload(message, GameCmdEnvelope.Refuse(0));
    }

    private static void WriteText(BinaryWriter writer, byte[] text)
    {
        writer.Write(text.Length);
        writer.Write(text);
    }
}

public sealed class GuildRosterHandler(ICommandBus commands)
    : ClientMessageHandler<GuildRosterRequest>, IProtocolCommandMessageHandler<GameGuildRosterCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuildRosterRequest message, CancellationToken ct)
        => _ = await commands.SendAsync(new GameGuildRosterCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildRosterRegistration
{
    internal static void AddGuildRosterSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GuildMemberListDecoder>(ClientMessageType.GameGuildMemberList);
        services.AddGameCmdMessageDecoder<GuildAllMemberListDecoder>(ClientMessageType.GameGuildAllMemberList);
        services.AddGameCmdMessageEncoder<GuildRosterReply, GuildRosterReplyEncoder>();
        services.AddProtocolCommandHandler<GameGuildRosterCommand, bool, GameGuildRosterCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildMemberList, typeof(GuildRosterRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildAllMemberList, typeof(GuildRosterRequest)));
        services.RegisterProtocolMessageHandler<GuildRosterHandler, GuildRosterRequest, GameGuildRosterCommand>(ClientMessageType.GameGuildMemberList);
        services.RegisterProtocolMessageHandler<GuildRosterHandler, GuildRosterRequest, GameGuildRosterCommand>(ClientMessageType.GameGuildAllMemberList);
    }
}
