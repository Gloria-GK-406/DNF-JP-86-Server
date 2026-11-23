using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ExpertJobs;

internal sealed record DisjointMachineRequest(GameMessageMetadata Metadata, ClientMessageType MessageType,
    bool Malformed, ushort OwnerUserId = 0, short TargetSlot = 0) : IClientNetworkMessage;

internal sealed class DisjointMachineDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var type = (ClientMessageType)((metadata.Selector << 16) | metadata.Type);
        if (type is ClientMessageType.GameDisjointMachineRepair or ClientMessageType.GameDisjointMachineUpgrade)
            return new DisjointMachineRequest(metadata, type, !body.IsEmpty);
        if (type != ClientMessageType.GameDisjointMachineUse || body.Length != 5
            || body[4] != (byte)InventoryContainerType.Main)
            return new DisjointMachineRequest(metadata, type, true);
        var owner = BinaryPrimitives.ReadUInt16LittleEndian(body);
        var slot = BinaryPrimitives.ReadInt16LittleEndian(body[2..]);
        return new DisjointMachineRequest(metadata, type, owner == 0 || slot < 0, owner, slot);
    }
}

internal sealed record DisjointMachineReply(ServerMessageType MessageType, byte Error = 0,
    short TargetSlot = 0, DisjointMachineUseReceipt? Use = null, ExpertMachineMaintenanceResult? Maintenance = null)
    : IGameCmdMessage;

internal sealed class DisjointMachineReplyEncoder : GameCmdMessageEncoder<DisjointMachineReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DisjointMachineReply message)
    {
        if (message.Error != 0) return OnePayload(message, new byte[] { 0, message.Error });
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        if (message.Use is { } use)
        {
            writer.Write(message.TargetSlot);
            writer.Write((byte)InventoryContainerType.Main);
            writer.Write(checked((byte)use.Rewards.Count));
            foreach (var reward in use.Rewards)
            {
                var destination = WireSlot(reward.Slot);
                writer.Write(checked((short)destination.Index));
                writer.Write(reward.ItemId);
                writer.Write(reward.Count);
            }
            writer.Write(use.RequesterGold);
            writer.Write(use.Endurance);
        }
        else if (message.Maintenance is { } maintenance)
        {
            writer.Write(maintenance.Gold);
            if (message.MessageType == ServerMessageType.GameDisjointMachineUpgrade)
                writer.Write(maintenance.Grade);
            writer.Write(maintenance.Endurance);
        }
        else
        {
            throw new InvalidOperationException("Successful machine result has no payload.");
        }
        return OnePayload(message, payload.ToArray());
    }

    private static GameSlot WireSlot(BusinessSlot? source)
    {
        var slot = A21InventoryLayout.ToGameSlot(source ?? throw new InvalidOperationException("Machine reward must be in inventory."));
        if (slot.Space != InventoryContainerType.Main)
            throw new InvalidOperationException("Machine reward must be in the main inventory list.");
        return slot;
    }
}

internal sealed record DisjointMachineOwnerChanged(int Gold, int Endurance) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameDisjointMachineOwnerChanged;
}

internal sealed class DisjointMachineOwnerChangedEncoder : GameNotiMessageEncoder<DisjointMachineOwnerChanged>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DisjointMachineOwnerChanged message)
    {
        var body = new byte[9];
        body[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), message.Gold);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5), message.Endurance);
        return OnePayload(message, body);
    }
}

internal sealed record DisjointMachineOwnerProjection(SelectedSessionIdentity Owner, bool ThirdParty,
    DisjointMachineUseReceipt Receipt) : INotification<DisjointMachineOwnerProjection>;

internal sealed class DisjointMachineOwnerProjectionHandler(ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService views) : INotificationHandler<DisjointMachineOwnerProjection>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, DisjointMachineOwnerProjection notice, CancellationToken ct)
    {
        if (recipient.ExpectedSession != notice.Owner || !sessions.IsInWorld(notice.Owner)) return [];
        var result = new List<IServerNetworkMessage>();
        if (notice.ThirdParty)
            result.Add(new DisjointMachineOwnerChanged(notice.Receipt.OwnerGold, notice.Receipt.Endurance));
        if (notice.Receipt.ExperienceChanged && await views.TryLoadAsync(notice.Owner, ct).ConfigureAwait(false) is { } view)
        {
            var equipment = view.CharacterInventory.Containers
                .SingleOrDefault(container => container.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
            result.Add(new CurrentUserStateMessage(view.Character, view.CharacterEntry.Public, equipment, view.CharacterEntry.Features.Pets));
        }
        return result;
    }
}

internal sealed record GameDisjointMachineCommand(NetworkDispatchContext Dispatch, DisjointMachineRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameDisjointMachineCommandHandler(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService online, IExpertJobDomainService jobs, IExpertStoreGameplayDomainService gameplay,
    ITownAreaDomainService townAreas, ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox)
    : ICommandHandler<GameDisjointMachineCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameDisjointMachineCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var request = command.Input;
        var reply = (ServerMessageType)request.MessageType;
        if (request.Malformed || !sessions.TryGetInWorld(connection.ConnectionId, out var actor))
            return await ReplyAsync(connection.ConnectionId, new(reply, (byte)(request.MessageType == ClientMessageType.GameDisjointMachineRepair ? 60 : 19)),
                ct).ConfigureAwait(false);
        var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
        return request.MessageType == ClientMessageType.GameDisjointMachineUse
            ? await DisjointAsync(execution, recipient, request, ct).ConfigureAwait(false)
            : await MaintainAsync(execution, recipient, reply, ct).ConfigureAwait(false);
    }

    private async ValueTask<bool> DisjointAsync(CommandExecutionContext execution, SelectedSessionRecipient recipient,
        DisjointMachineRequest request, CancellationToken ct)
    {
        const ServerMessageType reply = ServerMessageType.GameDisjointMachineUse;
        var actor = recipient.Session;
        if (gameplay.FindUse(actor, request.OwnerUserId, ExpertStoreKind.DisjointMachine) is not { } visit)
            return await ReplyAsync(actor.ConnectionId, new(reply, 19), ct).ConfigureAwait(false);
        var owner = visit.Store.Owner;
        var self = owner == actor;
        SelectedSessionRecipient[] participants;
        BusinessSlot slot;
        try
        {
            slot = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, request.TargetSlot));
            participants = self ? [recipient] : [recipient, GameParticipantCapture.Capture(online, owner)];
        }
        catch (InventorySlotMappingException)
        {
            return await ReplyAsync(actor.ConnectionId, new(reply, 19), ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return await ReplyAsync(actor.ConnectionId, new(reply, 19), ct).ConfigureAwait(false);
        }
        SelectedSessionIdentity[] owners = self ? [actor] : [actor, owner];
        var result = await units.WriteAsync(execution,
            new("disjoint-machine-use", participants, Footprint: CommandFootprintMode.DeclaredNonRequest, FootprintOwners: owners),
            async (unit, token) =>
            {
                var disjointer = await jobs.Get(owner.CharacterId).GetDisjointerAsync(token).ConfigureAwait(false);
                return disjointer is null ? new DisjointMachineUseResult(DisjointMachineRejection.InvalidStore)
                    : await disjointer.DisjointAsync(visit, slot, unit.Footprint, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        if (result.Receipt is not { } receipt)
        {
            var error = result.Rejection switch
            {
                DisjointMachineRejection.InventoryFull => (byte)4,
                DisjointMachineRejection.InsufficientGold => (byte)21,
                DisjointMachineRejection.GradeTooLow => (byte)0xD4,
                DisjointMachineRejection.Exhausted => (byte)189,
                _ => (byte)19,
            };
            return await ReplyAsync(actor.ConnectionId, new(reply, error), ct).ConfigureAwait(false);
        }

        var frames = new List<IGameServerMessage> { new DisjointMachineReply(reply, TargetSlot: request.TargetSlot, Use: receipt) };
        foreach (var group in receipt.InventoryChanges.Select(change => (Slot: A21InventoryLayout.ToGameSlot(change.Slot), change.Core))
                     .GroupBy(change => change.Slot.Space))
        {
            foreach (var item in group.Where(change => change.Core is null))
                frames.Add(GameInventoryDelivery.ClearedSlotNotification(group.Key, item.Slot.Index, receipt.PreparedAt));
            var updated = group.Where(change => change.Core is not null)
                .Select(change => new InventoryItemState(change.Core!.ItemUid, change.Slot.Index, change.Core.ToCore())).ToArray();
            if (updated.Length != 0)
                frames.Add(GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(group.Key, updated, receipt.PreparedAt)));
        }
        if (!self && visit.Store.Cost > 0)
            frames.Add(GameInventoryDelivery.CarriedGoldNotification(receipt.RequesterGold, receipt.PreparedAt));
        await outbox.RegisterExplicitAsync(actor.ConnectionId, new(frames, []), ct).ConfigureAwait(false);
        if (!self || receipt.ExperienceChanged)
            _ = await projections.ProjectAsync(new NotificationAudience.TheseSessions([owner]),
                new DisjointMachineOwnerProjection(owner, !self, receipt), ct).ConfigureAwait(false);
        if (receipt.ClosedStore is { } closed)
            await ExpertStoreProjection.ClosedAsync(sessions, townAreas, projections, closed, includeOwner: true, ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> MaintainAsync(CommandExecutionContext execution, SelectedSessionRecipient recipient,
        ServerMessageType reply, CancellationToken ct)
    {
        var actor = recipient.Session;
        var upgrade = reply == ServerMessageType.GameDisjointMachineUpgrade;
        var result = await units.WriteAsync(execution,
            new("disjoint-machine-maintenance", [recipient], Footprint: CommandFootprintMode.NonRequest),
            async (unit, token) =>
            {
                var disjointer = await jobs.Get(actor.CharacterId).GetDisjointerAsync(token).ConfigureAwait(false);
                if (disjointer is null) return new ExpertMachineMaintenanceResult(ExpertMachineMaintenanceRejection.InvalidState);
                return upgrade
                    ? await disjointer.UpgradeAsync(actor, unit.Footprint, token).ConfigureAwait(false)
                    : await disjointer.RepairAsync(actor, unit.Footprint, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        var error = result.Rejection switch
        {
            ExpertMachineMaintenanceRejection.None => (byte)0,
            ExpertMachineMaintenanceRejection.InsufficientGold or ExpertMachineMaintenanceRejection.CannotRepair => (byte)22,
            ExpertMachineMaintenanceRejection.CharacterLevelTooLow => (byte)14,
            _ => upgrade ? (byte)19 : (byte)60,
        };
        return await ReplyAsync(actor.ConnectionId, new(reply, error, Maintenance: result), ct).ConfigureAwait(false);
    }

    private async ValueTask<bool> ReplyAsync(NetworkConnectionId connection, DisjointMachineReply reply, CancellationToken ct)
    {
        await outbox.RegisterCmdAsync(connection, reply, ct).ConfigureAwait(false);
        return reply.Error == 0;
    }
}

internal sealed class DisjointMachineHandler(ICommandBus commands)
    : ClientMessageHandler<DisjointMachineRequest>, IProtocolCommandMessageHandler<GameDisjointMachineCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, DisjointMachineRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameDisjointMachineCommand(context, message), ct).ConfigureAwait(false);
}

internal static class DisjointMachineRegistration
{
    internal static void AddDisjointMachineSlice(this IServiceCollection services)
    {
        ClientMessageType[] types =
        [
            ClientMessageType.GameDisjointMachineUse,
            ClientMessageType.GameDisjointMachineRepair,
            ClientMessageType.GameDisjointMachineUpgrade,
        ];
        services.AddProtocolCommandHandler<GameDisjointMachineCommand, bool, GameDisjointMachineCommandHandler>(
            [.. types.Select(type => new ProtocolCommandMessageBinding(type, typeof(DisjointMachineRequest)))]);
        foreach (var type in types)
        {
            services.AddGameCmdMessageDecoder<DisjointMachineDecoder>(type);
            services.RegisterProtocolMessageHandler<DisjointMachineHandler, DisjointMachineRequest, GameDisjointMachineCommand>(type);
        }
        services.AddGameCmdMessageEncoder<DisjointMachineReply, DisjointMachineReplyEncoder>();
        services.AddGameNotiMessageEncoder<DisjointMachineOwnerChanged, DisjointMachineOwnerChangedEncoder>();
        services.AddNotificationHandler<DisjointMachineOwnerProjection, DisjointMachineOwnerProjectionHandler>();
    }
}
