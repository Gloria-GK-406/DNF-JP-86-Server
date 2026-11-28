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

internal sealed record EnchanterStoreRequest(GameMessageMetadata Metadata, ClientMessageType MessageType, bool Malformed,
    ushort OwnerUserId = 0, int RecipeId = 0, short CardSlot = -1, short TargetSlot = -1) : IClientNetworkMessage;

internal sealed class EnchanterStoreDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var type = (ClientMessageType)((metadata.Selector << 16) | metadata.Type);
        if (type == ClientMessageType.GameEnchanterStoreRepair)
            return new EnchanterStoreRequest(metadata, type, !body.IsEmpty);
        if (type != ClientMessageType.GameEnchanterStoreUse || body.Length != 13 || body[6] != 2
            || body[7] != (byte)InventoryContainerType.Main || body[10] != (byte)InventoryContainerType.Main)
            return new EnchanterStoreRequest(metadata, type, true);
        var owner = BinaryPrimitives.ReadUInt16LittleEndian(body);
        var recipe = BinaryPrimitives.ReadInt32LittleEndian(body[2..]);
        var target = BinaryPrimitives.ReadInt16LittleEndian(body[8..]);
        var card = BinaryPrimitives.ReadInt16LittleEndian(body[11..]);
        return new EnchanterStoreRequest(metadata, type, owner == 0 || recipe <= 0 || target < 0 || card < 0 || target == card,
            owner, recipe, card, target);
    }
}

internal sealed record EnchanterStoreReply(ServerMessageType MessageType, byte Error,
    EnchanterStoreUseReceipt? Use = null, ExpertMachineMaintenanceResult? Maintenance = null) : IGameCmdMessage;

internal sealed class EnchanterStoreReplyEncoder : GameCmdMessageEncoder<EnchanterStoreReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(EnchanterStoreReply message)
    {
        if (message.Error != 0) return OnePayload(message, new byte[] { 0, message.Error });
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        writer.Write((byte)1);
        if (message.Use is { } use)
        {
            writer.Write((byte)(use.Succeeded ? 1 : 0));
            writer.Write(use.Experience);
            writer.Write((byte)0);
            writer.Write(use.Endurance);
        }
        else if (message.Maintenance is { } maintenance)
        {
            writer.Write(maintenance.Gold);
            writer.Write(maintenance.Endurance);
        }
        else throw new InvalidDataException("Successful enchant shop reply has no result.");
        return OnePayload(message, body.ToArray());
    }
}

internal sealed record EnchanterStoreOwnerChanged(int Gold, int Endurance) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEnchanterStoreOwnerChanged;
}

internal sealed class EnchanterStoreOwnerChangedEncoder : GameNotiMessageEncoder<EnchanterStoreOwnerChanged>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(EnchanterStoreOwnerChanged message)
    {
        var body = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(body, message.Gold);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), message.Endurance);
        return OnePayload(message, body);
    }
}

internal sealed record EnchanterStoreOwnerProjection(SelectedSessionIdentity Owner, bool ThirdParty, bool Paid,
    EnchanterStoreUseReceipt Receipt) : INotification<EnchanterStoreOwnerProjection>;

internal sealed class EnchanterStoreOwnerProjectionHandler(ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService views) : INotificationHandler<EnchanterStoreOwnerProjection>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        EnchanterStoreOwnerProjection notice, CancellationToken ct)
    {
        if (recipient.ExpectedSession != notice.Owner || !sessions.IsInWorld(notice.Owner)) return [];
        var receipt = notice.Receipt;
        var frames = new List<IServerNetworkMessage>();
        if (notice.ThirdParty) frames.Add(new EnchanterStoreOwnerChanged(receipt.OwnerGold, receipt.Endurance));
        if (notice.Paid) frames.Add(GameInventoryDelivery.CarriedGoldNotification(receipt.OwnerGold, receipt.PreparedAt));
        if ((receipt.ExperienceChanged || receipt.LevelChanged)
            && await views.TryLoadAsync(notice.Owner, ct).ConfigureAwait(false) is { } view)
        {
            if (receipt.ExperienceChanged)
            {
                var equipment = view.CharacterInventory.Containers
                    .SingleOrDefault(container => container.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
                frames.Add(new CurrentUserStateMessage(view.Character, view.CharacterEntry.Public, equipment, view.CharacterEntry.Features.Pets));
            }
            if (receipt.LevelChanged) frames.Add(new ExpertJobStateMessage(view.CharacterEntry.Features.ExpertJob));
        }
        return frames;
    }
}

internal sealed record GameEnchanterStoreCommand(NetworkDispatchContext Dispatch, EnchanterStoreRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameEnchanterStoreCommandHandler(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService online, IExpertJobDomainService jobs, IExpertStoreGameplayDomainService gameplay,
    ITownAreaDomainService areas, ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox)
    : ICommandHandler<GameEnchanterStoreCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameEnchanterStoreCommand command,
        CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var request = command.Input;
        var repair = request.MessageType == ClientMessageType.GameEnchanterStoreRepair;
        var reply = (ServerMessageType)request.MessageType;
        if (request.Malformed || !sessions.TryGetInWorld(connection.ConnectionId, out var actor))
            return await ReplyAsync(connection.ConnectionId, new(reply, repair ? (byte)60 : (byte)13), ct).ConfigureAwait(false);
        var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
        return repair
            ? await RepairAsync(execution, recipient, ct).ConfigureAwait(false)
            : await EnchantAsync(execution, recipient, request, ct).ConfigureAwait(false);
    }

    private async ValueTask<bool> EnchantAsync(CommandExecutionContext execution, SelectedSessionRecipient recipient,
        EnchanterStoreRequest request, CancellationToken ct)
    {
        const ServerMessageType reply = ServerMessageType.GameEnchanterStoreUse;
        var actor = recipient.Session;
        if (gameplay.FindUse(actor, request.OwnerUserId, ExpertStoreKind.EnchantShop) is not { } visit)
            return await ReplyAsync(actor.ConnectionId, new(reply, 19), ct).ConfigureAwait(false);
        var owner = visit.Store.Owner;
        var self = owner == actor;
        BusinessSlot card;
        BusinessSlot target;
        SelectedSessionRecipient[] participants;
        try
        {
            card = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, request.CardSlot));
            target = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, request.TargetSlot));
            participants = self ? [recipient] : [recipient, GameParticipantCapture.Capture(online, owner)];
        }
        catch (InventorySlotMappingException)
        {
            return await ReplyAsync(actor.ConnectionId, new(reply, 13), ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return await ReplyAsync(actor.ConnectionId, new(reply, 19), ct).ConfigureAwait(false);
        }
        SelectedSessionIdentity[] owners = self ? [actor] : [actor, owner];
        var result = await units.WriteAsync(execution,
            new("enchanter-store-use", participants, Footprint: CommandFootprintMode.DeclaredNonRequest, FootprintOwners: owners),
            async (unit, token) =>
            {
                var enchanter = await jobs.Get(owner.CharacterId).GetEnchanterAsync(token).ConfigureAwait(false);
                return enchanter is null ? new EnchanterStoreUseResult(EnchanterStoreRejection.InvalidState)
                    : await enchanter.EnchantAsync(visit, request.RecipeId, card, target, unit.Footprint, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        if (result.Receipt is not { } receipt)
        {
            var error = result.Rejection switch
            {
                EnchanterStoreRejection.InvalidItem => (byte)13,
                EnchanterStoreRejection.InsufficientGold => (byte)21,
                EnchanterStoreRejection.InsufficientMaterials => (byte)22,
                EnchanterStoreRejection.Exhausted => (byte)189,
                _ => (byte)19,
            };
            return await ReplyAsync(actor.ConnectionId, new(reply, error), ct).ConfigureAwait(false);
        }

        var frames = new List<IGameServerMessage>();
        foreach (var change in receipt.InventoryChanges.OrderBy(change => change.Slot == card ? 0 : 1))
        {
            var slot = A21InventoryLayout.ToGameSlot(change.Slot);
            frames.Add(change.Core is { } core
                ? GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(slot.Space,
                    [new InventoryItemState(core.ItemUid, slot.Index, core.ToCore())], receipt.PreparedAt))
                : GameInventoryDelivery.ClearedSlotNotification(slot.Space, slot.Index, receipt.PreparedAt));
        }
        frames.Add(new EnchanterStoreReply(reply, 0, Use: receipt));
        var paid = !self && visit.Store.Cost > 0;
        if (paid) frames.Add(GameInventoryDelivery.CarriedGoldNotification(receipt.GuestGold, receipt.PreparedAt));
        await outbox.RegisterExplicitAsync(actor.ConnectionId, new(frames, []), ct).ConfigureAwait(false);
        if (!self || receipt.ExperienceChanged || receipt.LevelChanged)
            _ = await projections.ProjectAsync(new NotificationAudience.TheseSessions([owner]),
                new EnchanterStoreOwnerProjection(owner, !self, paid, receipt), ct).ConfigureAwait(false);
        if (receipt.ClosedStore is { } closed)
            await ExpertStoreProjection.ClosedAsync(sessions, areas, projections, closed, includeOwner: true, ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> RepairAsync(CommandExecutionContext execution, SelectedSessionRecipient recipient,
        CancellationToken ct)
    {
        var actor = recipient.Session;
        var result = await units.WriteAsync(execution,
            new("enchanter-store-repair", [recipient], Footprint: CommandFootprintMode.NonRequest),
            async (unit, token) =>
            {
                var enchanter = await jobs.Get(actor.CharacterId).GetEnchanterAsync(token).ConfigureAwait(false);
                return enchanter is null ? new ExpertMachineMaintenanceResult(ExpertMachineMaintenanceRejection.InvalidState)
                    : await enchanter.RepairAsync(actor, unit.Footprint, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        var error = result.Rejection switch
        {
            ExpertMachineMaintenanceRejection.None => (byte)0,
            ExpertMachineMaintenanceRejection.InsufficientGold or ExpertMachineMaintenanceRejection.CannotRepair => (byte)22,
            _ => (byte)60,
        };
        return await ReplyAsync(actor.ConnectionId, new(ServerMessageType.GameEnchanterStoreRepair, error, Maintenance: result),
            ct).ConfigureAwait(false);
    }

    private async ValueTask<bool> ReplyAsync(NetworkConnectionId connection, EnchanterStoreReply reply, CancellationToken ct)
    {
        await outbox.RegisterCmdAsync(connection, reply, ct).ConfigureAwait(false);
        return reply.Error == 0;
    }
}

internal sealed class EnchanterStoreHandler(ICommandBus commands)
    : ClientMessageHandler<EnchanterStoreRequest>, IProtocolCommandMessageHandler<GameEnchanterStoreCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, EnchanterStoreRequest message,
        CancellationToken ct) => _ = await commands.SendAsync(new GameEnchanterStoreCommand(context, message), ct).ConfigureAwait(false);
}

internal static class EnchanterStoreRegistration
{
    internal static void AddEnchanterStoreSlice(this IServiceCollection services)
    {
        ClientMessageType[] types = [ClientMessageType.GameEnchanterStoreUse, ClientMessageType.GameEnchanterStoreRepair];
        services.AddProtocolCommandHandler<GameEnchanterStoreCommand, bool, GameEnchanterStoreCommandHandler>(
            [.. types.Select(type => new ProtocolCommandMessageBinding(type, typeof(EnchanterStoreRequest)))]);
        foreach (var type in types)
        {
            services.AddGameCmdMessageDecoder<EnchanterStoreDecoder>(type);
            services.RegisterProtocolMessageHandler<EnchanterStoreHandler, EnchanterStoreRequest, GameEnchanterStoreCommand>(type);
        }
        services.AddGameCmdMessageEncoder<EnchanterStoreReply, EnchanterStoreReplyEncoder>();
        services.AddGameNotiMessageEncoder<EnchanterStoreOwnerChanged, EnchanterStoreOwnerChangedEncoder>();
        services.AddNotificationHandler<EnchanterStoreOwnerProjection, EnchanterStoreOwnerProjectionHandler>();
    }
}
