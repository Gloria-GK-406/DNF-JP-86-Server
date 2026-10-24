using System.Buffers.Binary;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Teleport;

public sealed record TeleportRequest(GameMessageMetadata Metadata, short SourceSlot, int ItemId,
    byte Reserved, byte TownId, byte AreaId, short X, short Y) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameTeleport;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.TownLocation;
    // Current town-selection UI sends area 0 and default-position sentinels. The server derives the gate.
    public bool IsSupported => SourceSlot >= 0 && ItemId > 0 && Reserved == 0 && AreaId == 0
        && X == short.MaxValue && Y == short.MaxValue;
}
internal sealed class TeleportDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 13) throw new InvalidDataException("TELEPORT expects thirteen A21 bytes.");
        return new TeleportRequest(metadata, BinaryPrimitives.ReadInt16LittleEndian(body),
            BinaryPrimitives.ReadInt32LittleEndian(body[2..]), body[6], body[7], body[8],
            BinaryPrimitives.ReadInt16LittleEndian(body[9..]), BinaryPrimitives.ReadInt16LittleEndian(body[11..]));
    }
}
public sealed record TeleportResponse(byte ErrorCode = 19) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameTeleport;
}
internal sealed class TeleportEncoder : GameCmdMessageEncoder<TeleportResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TeleportResponse message) =>
        OnePayload(0x00ED, new byte[] { 0, message.ErrorCode });
}
public sealed record TeleportCooldownNotification(int ItemId, int Seconds) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameLoadCooltimeItems;
}
internal sealed class TeleportCooldownEncoder : GameNotiMessageEncoder<TeleportCooldownNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TeleportCooldownNotification message)
    {
        if (message.ItemId <= 0 || message.Seconds < 0) throw new InvalidDataException("Invalid teleport cooldown projection.");
        var body = new byte[9]; body[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), message.ItemId);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5), message.Seconds);
        return OnePayload(0x00AC, body);
    }
}
public sealed record TeleportCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    BusinessSlot Source, int ItemId, byte TownId, ChannelEntryIdentity Channel)
    : ICommand<TeleportConsumableResult>, IRequestSettlementCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class TeleportCommandHandler(ITeleportConsumableDomainService teleports)
    : ICommandHandler<TeleportCommand, TeleportConsumableResult>
{
    public ValueTask<TeleportConsumableResult> HandleAsync(CommandExecutionContext context, TeleportCommand command,
        CancellationToken ct) => teleports.UseAsync(command.Session, command.Source, command.ItemId,
            command.TownId, command.Channel, context.LegacyFootprint, ct);
}
public sealed class TeleportHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ISelectedCharacterCurrentViewDomainService currentViews, ITownAreaDomainService townAreas,
    IInventorySlotMappingRuleService mappings, IChannelEntryPolicy channels, ICommandBus commands,
    TownAreaProjectionDelivery townProjection,
    IGameMessageSender sender, ILogger<TeleportHandler> logger) : ClientMessageHandler<TeleportRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, TeleportRequest request, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !request.IsSupported
            || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online) || online is null)
        {
            await sender.SendCmdAsync(context.Connection.ConnectionId, new TeleportResponse(), ct).ConfigureAwait(false);
            return;
        }
        var view = await currentViews.TryLoadAsync(session, ct).ConfigureAwait(false);
        var previous = townAreas.Find(session.CharacterId);
        if (view is null || previous is null || !session.Owns(previous)) return;
        BusinessSlot source;
        try { source = mappings.ToBusinessSlot(new(InventoryContainerType.Main, request.SourceSlot)); }
        catch (InventorySlotMappingException)
        {
            await sender.SendCmdAsync(context.Connection.ConnectionId, new TeleportResponse(), ct).ConfigureAwait(false);
            return;
        }
        var channel = await channels.ResolveAsync(context.Connection.ListenerPort, ct).ConfigureAwait(false);
        var result = await commands.SendAsync(new TeleportCommand(online.OnlineUserId, session, source,
            request.ItemId, request.TownId, channel), ct).ConfigureAwait(false);
        if (result.Receipt is not { } receipt)
        {
            logger.LogWarning("TELEPORT refused character={Character} item={Item} town={Town} reason={Reason}",
                session.CharacterId, request.ItemId, request.TownId, result.Status);
            await sender.SendCmdAsync(context.Connection.ConnectionId, new TeleportResponse(), ct).ConfigureAwait(false);
            return;
        }
        var wire = mappings.ToGameSlot(receipt.Source);
        var messages = new List<IGameServerMessage>();
        if (receipt.Cooldown > TimeSpan.Zero)
            messages.Add(new TeleportCooldownNotification(receipt.ItemId, checked((int)Math.Ceiling(receipt.Cooldown.TotalSeconds))));
        if (receipt.RemainingCore is null)
            messages.Add(GameInventoryDelivery.ClearedSlotNotification(wire.Space, wire.Index, receipt.At));
        else
            messages.AddRange(GameInventoryDelivery.Notifications([new(wire.Space,
                [new InventoryItemState(receipt.Identity, wire.Index, receipt.RemainingCore.ToCore())], receipt.At)]));
        await sender.SendBatchAsync(context.Connection, messages, ct).ConfigureAwait(false);
        if (!await townProjection.ProjectTransferAsync(context.Connection.ConnectionId, session, view, previous, ct).ConfigureAwait(false))
            logger.LogWarning("TELEPORT committed but town projection lost its membership character={Character}", session.CharacterId);
        logger.LogInformation("TELEPORT character={Character} item={Item} remaining={Remaining} destination={Town}:{Area} ({X},{Y})",
            session.CharacterId, receipt.ItemId, receipt.Remaining, receipt.Position.TownId, receipt.Position.AreaId,
            receipt.Position.X, receipt.Position.Y);
    }
}
