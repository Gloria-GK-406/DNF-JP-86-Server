using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildWarehouse;

public sealed record GuildWarehouseMaintenanceRequest(ClientMessageType MessageType, GameMessageMetadata Metadata, bool Valid,
    ushort SourceSlot = 0, int SourceItem = 0, ushort TargetSlot = 0, int TargetItem = 0) : IClientNetworkMessage;

internal sealed class GuildWarehouseMoveDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        const ClientMessageType type = ClientMessageType.GameGuildWarehouseMove;
        if (body.Length != 12)
            return new GuildWarehouseMaintenanceRequest(type, metadata, false);
        var sourceItem = BinaryPrimitives.ReadInt32LittleEndian(body[2..]);
        var targetItem = BinaryPrimitives.ReadInt32LittleEndian(body[8..]);
        return new GuildWarehouseMaintenanceRequest(type, metadata, sourceItem > 0 && targetItem >= -1,
            BinaryPrimitives.ReadUInt16LittleEndian(body), sourceItem,
            BinaryPrimitives.ReadUInt16LittleEndian(body[6..]), Math.Max(0, targetItem));
    }
}

internal sealed class GuildWarehouseHistoryDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildWarehouseMaintenanceRequest(ClientMessageType.GameGuildWarehouseHistory, metadata, body.IsEmpty);
}

internal sealed record GuildWarehouseMoveReply(ushort SourceSlot, ushort TargetSlot, GuildWarehouseFailure Failure)
    : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildWarehouseMoveResult;
}

internal sealed class GuildWarehouseMoveEncoder : GameCmdMessageEncoder<GuildWarehouseMoveReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildWarehouseMoveReply message)
    {
        if (message.Failure != GuildWarehouseFailure.None)
            return OnePayload(message, GameCmdEnvelope.Refuse(message.Failure switch
            {
                GuildWarehouseFailure.AccessDenied => 0x24,
                GuildWarehouseFailure.InvalidSlot => 0xC5,
                _ => 0xCD,
            }));
        Span<byte> body = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(body, message.SourceSlot);
        BinaryPrimitives.WriteUInt16LittleEndian(body[2..], message.TargetSlot);
        return OnePayload(message, GameCmdEnvelope.Accept(body));
    }
}

internal sealed record GuildWarehouseHistoryReply(IReadOnlyList<GuildWarehouseHistoryEntry>? Entries) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildWarehouseHistory;
}

internal sealed class GuildWarehouseHistoryEncoder : GameCmdMessageEncoder<GuildWarehouseHistoryReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildWarehouseHistoryReply message)
    {
        if (message.Entries is not { } entries)
            return OnePayload(message, GameCmdEnvelope.Refuse(0x24));

        const int entryWidth = 57;
        var body = new byte[4 + entries.Count * entryWidth];
        BinaryPrimitives.WriteInt32LittleEndian(body, entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var row = body.AsSpan(4 + i * entryWidth, entryWidth);
            BinaryPrimitives.WriteUInt32LittleEndian(row, checked((uint)entry.OccurredAt.ToUnixTimeSeconds()));
            row[4] = (byte)entry.Action;
            entry.ActorName.AsSpan(0, Math.Min(29, entry.ActorName.Length)).CopyTo(row[5..35]);
            BinaryPrimitives.WriteInt32LittleEndian(row[35..], entry.ItemId);
            BinaryPrimitives.WriteInt32LittleEndian(row[39..], entry.Count);
            // The final 14 bytes are optional equipment decoration data. A history row identifies the base item.
        }
        return OnePayload(message, GameCmdEnvelope.Accept(body));
    }
}

public sealed record GameGuildWarehouseMaintenanceCommand(NetworkDispatchContext Dispatch, GuildWarehouseMaintenanceRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameGuildWarehouseMaintenanceCommandHandler(ISelectedSessionDomainService sessions,
    IGuildWarehouseDomainService warehouse, ICommandWorkUnits units, IGameCommandOutbox outbox,
    ICommandProjections projections) : ICommandHandler<GameGuildWarehouseMaintenanceCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameGuildWarehouseMaintenanceCommand command,
        CancellationToken ct)
    {
        var input = command.Input;
        if (GameRequestFreshness.IsStale(input.Metadata))
            return false;
        var connection = command.Dispatch.Connection;
        var actor = sessions.TryGetInWorld(connection.ConnectionId, out var selected) ? selected : null;
        if (input.MessageType == ClientMessageType.GameGuildWarehouseHistory)
        {
            var history = input.Valid && actor is not null
                ? await units.ReadOnlyAsync(execution, new("guild-warehouse-history", [new(actor, connection, input.Metadata)]),
                    async token => new GuildWarehouseHistoryReply(await warehouse.HistoryAsync(actor, token).ConfigureAwait(false)),
                    ct).ConfigureAwait(false)
                : new GuildWarehouseHistoryReply(null);
            await outbox.RegisterCmdAsync(connection.ConnectionId, history, ct).ConfigureAwait(false);
            return history.Entries is not null;
        }

        var moved = !input.Valid ? new(GuildWarehouseFailure.InvalidSlot) :
            actor is null ? new GuildWarehouseMoveResult(GuildWarehouseFailure.AccessDenied) :
            await units.WriteAsync(execution, new("guild-warehouse-move", [new(actor, connection, input.Metadata)]),
                (_, token) => warehouse.MoveAsync(actor, input.SourceSlot, input.SourceItem, input.TargetSlot, input.TargetItem, token),
                ct).ConfigureAwait(false);
        await outbox.RegisterCmdAsync(connection.ConnectionId,
            new GuildWarehouseMoveReply(input.SourceSlot, input.TargetSlot, moved.Failure), ct).ConfigureAwait(false);
        if (moved.Warehouse is not { } view)
            return false;
        var snapshot = new GuildWarehouseCapacityChanged(view);
        await outbox.RegisterNotiAsync(connection.ConnectionId, snapshot, ct).ConfigureAwait(false);
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters(moved.OtherMembers ?? []), snapshot, ct)
            .ConfigureAwait(false);
        return true;
    }
}

public sealed class GuildWarehouseMaintenanceHandler(ICommandBus commands)
    : ClientMessageHandler<GuildWarehouseMaintenanceRequest>, IProtocolCommandMessageHandler<GameGuildWarehouseMaintenanceCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        GuildWarehouseMaintenanceRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildWarehouseMaintenanceCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildWarehouseMaintenanceRegistration
{
    internal static void Add(IServiceCollection services)
    {
        const ClientMessageType move = ClientMessageType.GameGuildWarehouseMove;
        const ClientMessageType history = ClientMessageType.GameGuildWarehouseHistory;
        services.AddGameCmdMessageDecoder<GuildWarehouseMoveDecoder>(move);
        services.AddGameCmdMessageDecoder<GuildWarehouseHistoryDecoder>(history);
        services.AddGameCmdMessageEncoder<GuildWarehouseMoveReply, GuildWarehouseMoveEncoder>();
        services.AddGameCmdMessageEncoder<GuildWarehouseHistoryReply, GuildWarehouseHistoryEncoder>();
        services.AddProtocolCommandHandler<GameGuildWarehouseMaintenanceCommand, bool, GameGuildWarehouseMaintenanceCommandHandler>(
            new ProtocolCommandMessageBinding(move, typeof(GuildWarehouseMaintenanceRequest)),
            new ProtocolCommandMessageBinding(history, typeof(GuildWarehouseMaintenanceRequest)));
        services.RegisterProtocolMessageHandler<GuildWarehouseMaintenanceHandler, GuildWarehouseMaintenanceRequest,
            GameGuildWarehouseMaintenanceCommand>(move);
        services.RegisterProtocolMessageHandler<GuildWarehouseMaintenanceHandler, GuildWarehouseMaintenanceRequest,
            GameGuildWarehouseMaintenanceCommand>(history);
    }
}
