using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildCreation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.GuildManagement;

public sealed record GuildRenameRequest(GameMessageMetadata Metadata, GameSlot? Source) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameGuildRename;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
}

internal sealed class GuildRenameDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 3 || body[2] != (byte)InventoryContainerType.Main)
            return new GuildRenameRequest(metadata, null);
        var slot = BinaryPrimitives.ReadUInt16LittleEndian(body);
        return new GuildRenameRequest(metadata, slot is >= 65 and <= 176 ? new GameSlot(InventoryContainerType.Main, slot) : null);
    }
}

internal sealed record GuildRenamed(string Name) : IGameNotiMessage, INotification<GuildRenamed>
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildRenamed;
}

internal sealed class GuildRenamedEncoder : GameNotiMessageEncoder<GuildRenamed>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildRenamed message)
    {
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        ChatDString.Write(writer, GuildCreationPayloadCodec.EncodeGuildText(message.Name));
        return OnePayload(message, body.ToArray());
    }
}

internal sealed class GuildRenamedHandler : INotificationHandler<GuildRenamed>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        GuildRenamed notice, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([notice]);
}

public sealed record GameGuildRenameCommand(NetworkDispatchContext Dispatch, GuildRenameRequest Request) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

public sealed class GuildRenameHandler(ICommandBus commands)
    : ClientMessageHandler<GuildRenameRequest>, IProtocolCommandMessageHandler<GameGuildRenameCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuildRenameRequest message,
        CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildRenameCommand(context, message), ct).ConfigureAwait(false);
}

internal sealed class GameGuildRenameCommandHandler(ISelectedSessionDomainService sessions,
    IGuildNameOperationSessionDomainService drafts, IGuildRenameDomainService renaming, IGuildOverviewDomainService overviews,
    ICharacterVisibleSessionDomainService visibility, ICommandWorkUnits units,
    IGameCommandOutbox outbox, ICommandProjections projections, TimeProvider clock,
    ILogger<GameGuildRenameCommandHandler> logger) : ICommandHandler<GameGuildRenameCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameGuildRenameCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        if (command.Request.Source is not { } source
            || context.Request is not CommandRequestContext.ProtocolEffectSelected selected
            || !sessions.IsInWorld(selected.Recipient.Session)
            || drafts.CheckedRename(selected.Recipient.Session) is not { } authorization)
            return await RefuseAsync().ConfigureAwait(false);

        var actor = selected.Recipient.Session;
        var write = await units.WriteResultAsync(context,
            new("guild-rename", [selected.Recipient], CommandWorkUnitKind.MainSettlement), async (unit, token) =>
            {
                try
                {
                    var receipt = await renaming.RenameAsync(actor, authorization, A21InventoryLayout.ToBusinessSlot(source),
                        unit.Footprint, token).ConfigureAwait(false);
                    var profile = receipt is null ? null
                        : await GuildProfileProjection.ReadAsync(overviews, actor.CharacterId, token).ConfigureAwait(false);
                    return new RenameResult(receipt, profile);
                }
                catch (Exception failure) when (failure is not OperationCanceledException
                    && !TransactionOutcomeUncertainException.Contains(failure))
                {
                    logger.LogWarning(failure, "Guild rename rolled back for character {Character}", actor.CharacterId.Value);
                    unit.RequireRollback(failure);
                    return new RenameResult(null, null);
                }
            }, ct).ConfigureAwait(false);
        if (write is not CommandWriteResult<RenameResult>.Returned { Value: var result }
            || result.Receipt is not { } renamed || result.Profile is not { } profile)
            return await RefuseAsync().ConfigureAwait(false);

        drafts.Cancel(actor);
        var now = clock.GetUtcNow();
        var update = renamed.RemainingCard is { } card
            ? GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(
                source.Space, [new InventoryItemState(card.ItemUid, source.Index, card.ToCore())], now))
            : GameInventoryDelivery.ClearedSlotNotification(source.Space, source.Index, now);
        await outbox.RegisterBatchAsync(connection, [new GuildManagementReply(ServerMessageType.GameGuildRename), update], ct)
            .ConfigureAwait(false);

        await profile.PublishAsync(sessions, visibility, projections, ct, authorization.Name, actor.CharacterId).ConfigureAwait(false);
        return true;

        async ValueTask<bool> RefuseAsync()
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildManagementReply(ServerMessageType.GameGuildRename, 0x22), ct)
                .ConfigureAwait(false);
            return false;
        }
    }

    private sealed record RenameResult(GuildRenameReceipt? Receipt, GuildProfileProjection? Profile);
}

internal static class GuildRenameRegistration
{
    internal static void AddGuildRenameSlice(this IServiceCollection services)
    {
        const ClientMessageType type = ClientMessageType.GameGuildRename;
        services.AddGameCmdMessageDecoder<GuildRenameDecoder>(type);
        services.AddGameNotiMessageEncoder<GuildRenamed, GuildRenamedEncoder>();
        services.AddNotificationHandler<GuildRenamed, GuildRenamedHandler>();
        services.AddProtocolCommandHandler<GameGuildRenameCommand, bool, GameGuildRenameCommandHandler>(
            new ProtocolCommandMessageBinding(type, typeof(GuildRenameRequest)));
        services.RegisterProtocolMessageHandler<GuildRenameHandler, GuildRenameRequest, GameGuildRenameCommand>(type);
    }
}
