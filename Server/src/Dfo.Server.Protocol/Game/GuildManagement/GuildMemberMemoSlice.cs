using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildCreation;
using Dfo.Server.Protocol.Game.GuildRoster;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildManagement;

public sealed record GuildMemberMemoRequest(GameMessageMetadata Metadata, string? Memo) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGuildSetMemberMemo;
}

internal sealed class GuildMemberMemoDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildMemberMemoRequest(metadata, GuildCreationPayloadCodec.DecodeText(body, 0, GuildMember.MaximumMemoBytes));
}

public sealed class GuildMemberMemoHandler(ICommandBus commands)
    : ClientMessageHandler<GuildMemberMemoRequest>, IProtocolCommandMessageHandler<GameGuildMemberMemoCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, GuildMemberMemoRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildMemberMemoCommand(context, message), ct).ConfigureAwait(false);
}

public sealed record GameGuildMemberMemoCommand(NetworkDispatchContext Dispatch, GuildMemberMemoRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildMemberMemoCommandHandler(
    ISelectedSessionDomainService sessions, IGuildMembershipDomainService guilds, IGuildMembershipQueryDomainService query,
    IChannelEntryDomainService channels, ICommandWorkUnits units, IGameCommandOutbox outbox,
    ICommandProjections projections) : ICommandHandler<GameGuildMemberMemoCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context, GameGuildMemberMemoCommand command, CancellationToken ct)
    {
        var request = command.Request;
        if (GameRequestFreshness.IsStale(request.Metadata))
            return false;

        var connection = command.Dispatch.Connection;
        if (!sessions.TryGetInWorld(connection.ConnectionId, out var actor))
            return await ReplyAsync(0x22).ConfigureAwait(false);
        if (request.Memo is not { } memo)
            return await ReplyAsync(0x6C).ConfigureAwait(false);

        var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
        var write = await units.WriteResultAsync(context, new("guild-member-memo", [recipient]), async (_, token) =>
        {
            var status = await guilds.SetMemberMemoAsync(actor.CharacterId, memo, token).ConfigureAwait(false);
            return status == GuildMutationStatus.Success
                ? new MemoResult(status, await query.ReadRosterEntriesAsync(actor.CharacterId, token).ConfigureAwait(false))
                : new MemoResult(status, []);
        }, ct).ConfigureAwait(false);

        if (write is not CommandWriteResult<MemoResult>.Returned { Value: var result })
            return await ReplyAsync(0x22).ConfigureAwait(false);
        if (result.Status != GuildMutationStatus.Success)
            return await ReplyAsync(result.Status == GuildMutationStatus.InvalidText ? (byte)0x6C : (byte)0x22)
                .ConfigureAwait(false);

        await ReplyAsync(null).ConfigureAwait(false);
        if (await GuildRosterProjection.BuildAsync(result.Roster, sessions, channels, ct).ConfigureAwait(false) is { } roster)
        {
            var audience = new NotificationAudience.TheseCharacters(
                result.Roster.Select(row => row.Membership.Character).ToArray());
            await projections.ProjectAsync(audience, new GuildRosterChanged(roster), ct).ConfigureAwait(false);
        }
        return true;

        async ValueTask<bool> ReplyAsync(byte? error)
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId,
                new GuildManagementReply(ServerMessageType.GameGuildSetMemberMemo, error), ct).ConfigureAwait(false);
            return !error.HasValue;
        }
    }

    private sealed record MemoResult(GuildMutationStatus Status, IReadOnlyList<GuildRosterEntry> Roster);
}

internal static class GuildMemberMemoRegistration
{
    internal static void AddGuildMemberMemoSlice(this IServiceCollection services)
    {
        const ClientMessageType type = ClientMessageType.GameGuildSetMemberMemo;
        services.AddGameCmdMessageDecoder<GuildMemberMemoDecoder>(type);
        services.AddProtocolCommandHandler<GameGuildMemberMemoCommand, bool, GameGuildMemberMemoCommandHandler>(
            new ProtocolCommandMessageBinding(type, typeof(GuildMemberMemoRequest)));
        services.RegisterProtocolMessageHandler<GuildMemberMemoHandler, GuildMemberMemoRequest, GameGuildMemberMemoCommand>(type);
    }
}
