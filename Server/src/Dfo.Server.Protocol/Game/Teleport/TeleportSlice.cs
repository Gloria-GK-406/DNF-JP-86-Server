using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
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
public sealed record GameTeleportCommand(NetworkDispatchContext Dispatch, TeleportRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal sealed partial class GameTeleportCommandHandler : ICommandHandler<GameTeleportCommand, bool>
{
    private readonly ISelectedSessionDomainService sessions;
    private readonly ITownAreaDomainService townAreas;
    private readonly ICommandProjections projections;
    private readonly IPartyProjectionStateDomainService projectionState;
    private readonly ILogger logger;
    private readonly ISelectedCharacterCurrentViewDomainService currentViews;
    private readonly IMarriageHousingQueryDomainService housing;
    private readonly IOnlineUserQueryDomainService onlineUsers;
    private readonly IPartyService parties;

    private readonly IOnlineUserQueryDomainService users;
    private readonly IInventorySlotMappingRuleService mappings;
    private readonly IChannelEntryDomainService channels;
    private readonly ITeleportConsumableDomainService teleports;
    private readonly ICommandWorkUnits units;
    private readonly IGameCommandOutbox outbox;
    private readonly Dfo.Server.Abstractions.Marriage.IMarriageHousingSessionDomainService housingSessions;
    public GameTeleportCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
        ISelectedCharacterCurrentViewDomainService currentViews, ITownAreaDomainService townAreas,
        IInventorySlotMappingRuleService mappings, IChannelEntryDomainService channels, ITeleportConsumableDomainService teleports,
        ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox,
        Dfo.Server.Abstractions.Parties.IPartyService parties, Dfo.Server.Abstractions.Networking.IPartyProjectionStateDomainService projectionState,
        ILogger<GameTeleportCommandHandler> logger, Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService housing,
        Dfo.Server.Abstractions.Marriage.IMarriageHousingSessionDomainService housingSessions)

    {
        this.sessions = sessions; this.townAreas = townAreas; this.projections = projections; this.projectionState = projectionState; this.logger = logger; this.currentViews = currentViews; this.housing = housing; this.onlineUsers = onlineUsers; this.parties = parties; this.users = onlineUsers; this.mappings = mappings; this.channels = channels; this.teleports = teleports;
      this.units = units; this.outbox = outbox; this.housingSessions = housingSessions; }
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameTeleportCommand command, CancellationToken ct) =>
        TeleportAsync(execution, command.Dispatch, command.Input, ct);
    private async ValueTask<bool> TeleportAsync(CommandExecutionContext execution, NetworkDispatchContext context, TeleportRequest request, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || execution.Request is CommandRequestContext.ProtocolEffectRejected || !request.IsSupported
            || !users.TryGetByConnection(context.Connection.ConnectionId, out var online) || online is null)
        {
            await outbox.RegisterCmdAsync(context.Connection.ConnectionId, new TeleportResponse(), ct).ConfigureAwait(false);
            return false;
        }
        var view = await currentViews.TryLoadAsync(session, ct).ConfigureAwait(false);
        var previous = townAreas.Find(session.CharacterId);
        if (view is null || previous is null || previous.Session != session) return false;
        BusinessSlot source;
        try { source = mappings.ToBusinessSlot(new(InventoryContainerType.Main, request.SourceSlot)); }
        catch (InventorySlotMappingException)
        {
            await outbox.RegisterCmdAsync(context.Connection.ConnectionId, new TeleportResponse(), ct).ConfigureAwait(false);
            return false;
        }
        var channel = await channels.ResolveAsync(context.Connection.ListenerPort, ct).ConfigureAwait(false);
        if (execution.Request is not CommandRequestContext.ProtocolEffectSelected selected || session != selected.Recipient.Session)
            throw new InvalidOperationException("Teleport requires its original selected actor.");
        var result = await units.WriteAsync(execution, new("Teleport", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            (unit, token) => teleports.UseAsync(session, source, request.ItemId, request.TownId, channel, unit.Footprint, token), ct).ConfigureAwait(false);
        if (result.Receipt is not { } receipt)
        {
            logger.LogWarning("TELEPORT refused character={Character} item={Item} town={Town} reason={Reason}",
                session.CharacterId, request.ItemId, request.TownId, result.Status);
            await outbox.RegisterCmdAsync(context.Connection.ConnectionId, new TeleportResponse(), ct).ConfigureAwait(false);
            return false;
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
        await outbox.RegisterBatchAsync(context.Connection, messages, ct).ConfigureAwait(false);
        if (!await TownMovementProjection.TownProjectTransferAsync(sessions, townAreas, projections, new PartyTownProjectionDelivery(parties, townAreas, sessions, projections, projectionState), currentViews, housing, outbox, housingSessions, context.Connection.ConnectionId, session, view, previous, ct).ConfigureAwait(false))
            logger.LogWarning("TELEPORT committed but town projection lost its membership character={Character}", session.CharacterId);
        logger.LogInformation("TELEPORT character={Character} item={Item} remaining={Remaining} destination={Town}:{Area} ({X},{Y})",
            session.CharacterId, receipt.ItemId, receipt.Remaining, receipt.Position.TownId, receipt.Position.AreaId,
            receipt.Position.X, receipt.Position.Y);
        return true;
    }
}
public sealed class TeleportHandler(ICommandBus commands) : ClientMessageHandler<TeleportRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameTeleportCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, TeleportRequest request, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameTeleportCommand(context, request), ct).ConfigureAwait(false);
}
