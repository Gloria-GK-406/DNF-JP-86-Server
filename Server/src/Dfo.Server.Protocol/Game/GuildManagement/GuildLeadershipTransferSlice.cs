using System.Buffers.Binary;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildInfo;
using Dfo.Server.Protocol.Game.GuildRankAssignment;
using Dfo.Server.Protocol.Game.GuildRoster;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildManagement;

public sealed record GuildLeadershipTransferRequest(GameMessageMetadata Metadata, byte[]? TargetName)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGuildTransferLeadership;
}

internal sealed class GuildLeadershipTransferDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length > sizeof(int) && BinaryPrimitives.ReadInt32LittleEndian(body) is >= 1 and <= 62 and var length
            && length == body.Length - sizeof(int))
            return new GuildLeadershipTransferRequest(metadata, body[sizeof(int)..].ToArray());

        return new GuildLeadershipTransferRequest(metadata, null);
    }
}

internal sealed record GuildLeadershipTransferred(byte[] Name)
    : IGameNotiMessage, INotification<GuildLeadershipTransferred>
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildLeadershipTransferred;
}

internal sealed class GuildLeadershipTransferredEncoder : GameNotiMessageEncoder<GuildLeadershipTransferred>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildLeadershipTransferred message)
    {
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        ChatDString.Write(writer, message.Name);
        return OnePayload(message, body.ToArray());
    }
}

internal sealed class GuildLeadershipTransferredHandler : INotificationHandler<GuildLeadershipTransferred>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildLeadershipTransferred notice, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([notice]);
}

public sealed class GuildLeadershipTransferHandler(ICommandBus commands)
    : ClientMessageHandler<GuildLeadershipTransferRequest>, IProtocolCommandMessageHandler<GameGuildLeadershipTransferCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, GuildLeadershipTransferRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildLeadershipTransferCommand(context, message), ct).ConfigureAwait(false);
}

public sealed record GameGuildLeadershipTransferCommand(NetworkDispatchContext Dispatch, GuildLeadershipTransferRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildLeadershipTransferCommandHandler(
    IGuildMembershipDomainService guilds, IGuildMembershipQueryDomainService query, IGuildOverviewDomainService overviews,
    ISelectedSessionDomainService sessions, IChannelEntryDomainService channels, ICommandWorkUnits units,
    IGameCommandOutbox outbox, ICommandProjections projections)
    : ICommandHandler<GameGuildLeadershipTransferCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context, GameGuildLeadershipTransferCommand command, CancellationToken ct)
    {
        var request = command.Request;
        if (GameRequestFreshness.IsStale(request.Metadata))
            return false;

        var connection = command.Dispatch.Connection;
        if (request.TargetName is not { } name || !sessions.TryGetInWorld(connection.ConnectionId, out var actor))
            return await ReplyAsync(0x22).ConfigureAwait(false);

        var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
        var write = await units.WriteResultAsync(context, new("guild-leadership-transfer", [recipient]), async (_, token) =>
        {
            var successor = await guilds.TransferLeadershipAsync(actor.CharacterId, name, token).ConfigureAwait(false);
            if (successor is null)
                return new TransferResult(null, GuildInfoPanel.Empty, []);

            var panel = await GuildInfoPanel.ReadAsync(overviews, actor.CharacterId, token).ConfigureAwait(false);
            var roster = await query.ReadRosterEntriesAsync(actor.CharacterId, token).ConfigureAwait(false);
            return new TransferResult(successor, panel, roster);
        }, ct).ConfigureAwait(false);

        if (write is not CommandWriteResult<TransferResult>.Returned { Value: var result }
            || result.Successor is not { } leader)
            return await ReplyAsync(0x22).ConfigureAwait(false);

        await ReplyAsync(null).ConfigureAwait(false);
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters([actor.CharacterId]),
            new GuildMemberRankAssigned(actor.CharacterId, GuildRankSettings.Member), ct).ConfigureAwait(false);
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters([leader]),
            new GuildMemberRankAssigned(leader, GuildRankSettings.Leader), ct).ConfigureAwait(false);

        var audience = new NotificationAudience.TheseCharacters(result.Roster.Select(row => row.Membership.Character).ToArray());
        await projections.ProjectAsync(audience, result.Panel, ct).ConfigureAwait(false);
        if (await GuildRosterProjection.BuildAsync(result.Roster, sessions, channels, ct).ConfigureAwait(false) is { } snapshot)
            await projections.ProjectAsync(audience, new GuildRosterChanged(snapshot), ct).ConfigureAwait(false);

        var others = audience.CharacterIds.Where(character => character != actor.CharacterId).ToArray();
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters(others),
            new GuildLeadershipTransferred(name), ct).ConfigureAwait(false);
        return true;

        async ValueTask<bool> ReplyAsync(byte? error)
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId,
                new GuildManagementReply(ServerMessageType.GameGuildTransferLeadership, error), ct).ConfigureAwait(false);
            return !error.HasValue;
        }
    }

    private sealed record TransferResult(CharacterId? Successor, GuildInfoPanel Panel, IReadOnlyList<GuildRosterEntry> Roster);
}

internal static class GuildLeadershipTransferRegistration
{
    internal static void AddGuildLeadershipTransferSlice(this IServiceCollection services)
    {
        const ClientMessageType type = ClientMessageType.GameGuildTransferLeadership;
        services.AddGameCmdMessageDecoder<GuildLeadershipTransferDecoder>(type);
        services.AddGameNotiMessageEncoder<GuildLeadershipTransferred, GuildLeadershipTransferredEncoder>();
        services.AddNotificationHandler<GuildLeadershipTransferred, GuildLeadershipTransferredHandler>();
        services.AddProtocolCommandHandler<GameGuildLeadershipTransferCommand, bool, GameGuildLeadershipTransferCommandHandler>(
            new ProtocolCommandMessageBinding(type, typeof(GuildLeadershipTransferRequest)));
        services.RegisterProtocolMessageHandler<GuildLeadershipTransferHandler, GuildLeadershipTransferRequest,
            GameGuildLeadershipTransferCommand>(type);
    }
}
