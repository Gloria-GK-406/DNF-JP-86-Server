using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildCreation;
using Dfo.Server.Protocol.Game.GuildInfo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.GuildManagement;

public sealed record GuildManagementRequest(
    ClientMessageType MessageType, GameMessageMetadata Metadata, bool Valid,
    string? Text = null, byte Rank = 0, uint PermissionMask = 0) : IClientNetworkMessage;

internal sealed class GuildPromotionDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var text = GuildCreationPayloadCodec.DecodeText(body, 0, 80);
        return new GuildManagementRequest(ClientMessageType.GameGuildSetPromotion, metadata, text is not null, text);
    }
}

internal sealed class GuildRankNameDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        const ClientMessageType type = ClientMessageType.GameGuildSetRankName;
        if (body.IsEmpty || !GuildRankSettings.IsMemberRank(body[0]))
            return new GuildManagementRequest(type, metadata, false);

        var name = GuildCreationPayloadCodec.DecodeText(body[1..], 1, GuildRankSettings.MaximumNameBytes);
        return new GuildManagementRequest(type, metadata, name is not null, name, body[0]);
    }
}

internal sealed class GuildRankPermissionsDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        const ClientMessageType type = ClientMessageType.GameGuildSetRankPermissions;
        if (body.Length != 1 + sizeof(uint) || !GuildRankSettings.IsMemberRank(body[0]))
            return new GuildManagementRequest(type, metadata, false);

        var permissions = BinaryPrimitives.ReadUInt32LittleEndian(body[1..]);
        return new GuildManagementRequest(type, metadata, (permissions & ~GuildRankSettings.ValidPermissionMask) == 0,
            Rank: body[0], PermissionMask: permissions);
    }
}

internal sealed record GuildManagementReply(ServerMessageType MessageType, byte? Error = null) : IGameCmdMessage;

internal sealed class GuildManagementReplyEncoder : GameCmdMessageEncoder<GuildManagementReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildManagementReply message) =>
        OnePayload(message, message.Error is { } error ? GameCmdEnvelope.Refuse(error) : GameCmdEnvelope.Accept([]));
}

public sealed class GuildManagementRequestHandler(ICommandBus commands)
    : ClientMessageHandler<GuildManagementRequest>, IProtocolCommandMessageHandler<GameGuildManagementCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, GuildManagementRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildManagementCommand(context, message), ct).ConfigureAwait(false);
}

public sealed record GameGuildManagementCommand(NetworkDispatchContext Dispatch, GuildManagementRequest Request) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildManagementCommandHandler(
    ISelectedSessionDomainService sessions,
    IGuildDomainService guilds,
    IGuildQueryDomainService query,
    IGuildProgressDomainService progress,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    ICommandProjections projections,
    ILogger<GameGuildManagementCommandHandler> logger) : ICommandHandler<GameGuildManagementCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context, GameGuildManagementCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var connection = command.Dispatch.Connection;
        var replyType = (ServerMessageType)(int)request.MessageType;
        if (!sessions.TryGetInWorld(connection.ConnectionId, out var actor))
            return await ReplyAsync(0x22).ConfigureAwait(false);
        if (!request.Valid)
            return await ReplyAsync(InvalidInputError(request.MessageType)).ConfigureAwait(false);

        var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
        var write = await units.WriteResultAsync(context, new("guild-management", [recipient]), async (unit, token) =>
        {
            try
            {
                var status = await (request.MessageType switch
                {
                    ClientMessageType.GameGuildSetPromotion => guilds.SetPromotionAsync(actor.CharacterId, request.Text!, token),
                    ClientMessageType.GameGuildSetRankName => guilds.SetRankNameAsync(actor.CharacterId, request.Rank, request.Text!, token),
                    ClientMessageType.GameGuildSetRankPermissions => guilds.SetRankPermissionsAsync(
                        actor.CharacterId, request.Rank, request.PermissionMask, token),
                    _ => throw new InvalidOperationException($"Unsupported guild management request {request.MessageType}."),
                }).ConfigureAwait(false);
                if (status != GuildMutationStatus.Success)
                    return new ManagementResult(status, GuildInfoPanel.Empty);

                var panel = await GuildInfoPanel.ReadAsync(query, progress, actor.CharacterId, token).ConfigureAwait(false);
                return new ManagementResult(status, panel);
            }
            catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
            {
                logger.LogWarning(error, "Guild management update rolled back for character {Character}", actor.CharacterId.Value);
                unit.RequireRollback(error);
                return new ManagementResult(GuildMutationStatus.NotMember, GuildInfoPanel.Empty);
            }
        }, ct).ConfigureAwait(false);

        if (write is CommandWriteResult<ManagementResult>.CommitRolledBack rollback)
        {
            logger.LogWarning(rollback.Cause, "Guild management commit rolled back for character {Character}", actor.CharacterId.Value);
            return await ReplyAsync(0x22).ConfigureAwait(false);
        }
        var result = ((CommandWriteResult<ManagementResult>.Returned)write).Value;
        if (result.Status is GuildMutationStatus.InvalidText or GuildMutationStatus.InvalidRankSetting)
            return await ReplyAsync(InvalidInputError(request.MessageType)).ConfigureAwait(false);
        if (result.Status != GuildMutationStatus.Success)
            return await ReplyAsync(0x22).ConfigureAwait(false);

        await ReplyAsync(null).ConfigureAwait(false);
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters(
            result.Panel.Members.Select(member => member.Character).ToArray()), result.Panel, ct).ConfigureAwait(false);
        return true;

        async ValueTask<bool> ReplyAsync(byte? error)
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildManagementReply(replyType, error), ct)
                .ConfigureAwait(false);
            return !error.HasValue;
        }
    }

    private static byte InvalidInputError(ClientMessageType type) =>
        type == ClientMessageType.GameGuildSetPromotion ? (byte)0x6C : (byte)0x24;

    private sealed record ManagementResult(GuildMutationStatus Status, GuildInfoPanel Panel);
}

internal static class GuildManagementRegistration
{
    internal static void AddGuildManagementSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GuildPromotionDecoder>(ClientMessageType.GameGuildSetPromotion);
        services.AddGameCmdMessageDecoder<GuildRankPermissionsDecoder>(ClientMessageType.GameGuildSetRankPermissions);
        services.AddGameCmdMessageDecoder<GuildRankNameDecoder>(ClientMessageType.GameGuildSetRankName);
        services.AddGameCmdMessageEncoder<GuildManagementReply, GuildManagementReplyEncoder>();
        services.AddProtocolCommandHandler<GameGuildManagementCommand, bool, GameGuildManagementCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildSetPromotion, typeof(GuildManagementRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildSetRankPermissions, typeof(GuildManagementRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildSetRankName, typeof(GuildManagementRequest)));
        services.RegisterProtocolMessageHandler<GuildManagementRequestHandler, GuildManagementRequest, GameGuildManagementCommand>(
            ClientMessageType.GameGuildSetPromotion);
        services.RegisterProtocolMessageHandler<GuildManagementRequestHandler, GuildManagementRequest, GameGuildManagementCommand>(
            ClientMessageType.GameGuildSetRankPermissions);
        services.RegisterProtocolMessageHandler<GuildManagementRequestHandler, GuildManagementRequest, GameGuildManagementCommand>(
            ClientMessageType.GameGuildSetRankName);
    }
}
