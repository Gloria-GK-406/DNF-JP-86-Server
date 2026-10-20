using System.Buffers.Binary;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.TitleBook;

public sealed record TitleBookMutationRequest(GameMessageMetadata Metadata, bool Store, int Space,
    int Slot, int ItemId, int Category, int Index, bool Complete) : IClientNetworkMessage
{
    public ClientMessageType MessageType => Store ? ClientMessageType.GameStoreTitle : ClientMessageType.GameWithdrawTitle;
}
internal abstract class TitleBookMutationDecoder(bool store) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => body.Length == 20
        ? new TitleBookMutationRequest(metadata, store, Read(body), Read(body[4..]), Read(body[8..]), Read(body[12..]), Read(body[16..]), true)
        : new TitleBookMutationRequest(metadata, store, 0, -1, 0, 0, -1, false);
    private static int Read(ReadOnlySpan<byte> body) => BinaryPrimitives.ReadInt32LittleEndian(body);
}
internal sealed class StoreTitleDecoder() : TitleBookMutationDecoder(true);
internal sealed class WithdrawTitleDecoder() : TitleBookMutationDecoder(false);
public sealed record TitleBookMutationResponse(bool Store, int Space, int Slot, int Category, int Index, byte? Error = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => Store ? ServerMessageType.GameStoreTitle : ServerMessageType.GameWithdrawTitle;
    internal byte[] Body()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Error is null ? (byte)1 : (byte)0);
        if (Error is { } error) writer.Write(error);
        writer.Write(Space);
        if (Error is null) writer.Write(Slot);
        writer.Write(Category);
        if (Error is null) writer.Write(Index);
        return stream.ToArray();
    }
}
internal sealed class TitleBookMutationEncoder : GameCmdMessageEncoder<TitleBookMutationResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TitleBookMutationResponse message) =>
        OnePayload(message.Store ? (ushort)0x019C : (ushort)0x019D, message.Body());
}
public sealed record StoreTitleCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    BusinessSlot Source, int ItemId, int Category, int Index) : ICommand<TitleBookMutationResult>, IOnlineUserCommand, IAccountCommand, ISelectedSessionCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed record WithdrawTitleCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    BusinessSlot Destination, int ItemId, int Category, int Index) : ICommand<TitleBookMutationResult>, IOnlineUserCommand, IAccountCommand, ISelectedSessionCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class StoreTitleCommandHandler(ITitleBookDomainService titles) : ICommandHandler<StoreTitleCommand, TitleBookMutationResult>
{
    public ValueTask<TitleBookMutationResult> HandleAsync(CommandExecutionContext context, StoreTitleCommand command, CancellationToken ct) =>
        titles.StoreAsync(command.Session.CharacterId, command.Source, command.ItemId, command.Category, command.Index, ct);
}
public sealed class WithdrawTitleCommandHandler(ITitleBookDomainService titles) : ICommandHandler<WithdrawTitleCommand, TitleBookMutationResult>
{
    public ValueTask<TitleBookMutationResult> HandleAsync(CommandExecutionContext context, WithdrawTitleCommand command, CancellationToken ct) =>
        titles.WithdrawAsync(command.Session.CharacterId, command.Destination, command.ItemId, command.Category, command.Index, ct);
}
// TBM1-only request; native ACK shape is retained, but no physical book transfer occurs.
public sealed record EquipCarriedTitleReplacingMirrorCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    BusinessSlot Source, int ItemId, int Category, int Index) : ICommand<TitleBookMutationResult>, IOnlineUserCommand, IAccountCommand, ISelectedSessionCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class EquipCarriedTitleReplacingMirrorCommandHandler(ITitleBookDomainService titles)
    : ICommandHandler<EquipCarriedTitleReplacingMirrorCommand, TitleBookMutationResult>
{
    public ValueTask<TitleBookMutationResult> HandleAsync(CommandExecutionContext context, EquipCarriedTitleReplacingMirrorCommand command, CancellationToken ct) =>
        titles.EquipCarriedTitleReplacingMirrorAsync(command.Session.CharacterId, command.Source, command.ItemId, command.Category, command.Index, ct);
}

// Bounded stripes keep committed title projections and request confirmations ordered.
// They coordinate only protocol delivery, never durable consumers.
public sealed class AchievementTitleProjectionGate
{
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();
    public async ValueTask<IDisposable> EnterAsync(CharacterId character, CancellationToken ct)
    {
        var gate = gates[(uint)character.Value % (uint)gates.Length];
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return new Lease(gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; gate.Release(); }
    }
}
internal sealed class TitleBookMutationHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    IInventorySlotMappingRuleService mapping, ICommandBus commands, IGameMessageSender sender,
    AchievementTitleProjectionGate gate, ILogger<TitleBookMutationHandler> logger) : ClientMessageHandler<TitleBookMutationRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, TitleBookMutationRequest message, CancellationToken ct)
    {
        var replaceMirror = !message.Store && message.Space == 0x314D4254;
        ValueTask Refuse() => sender.SendCmdAsync(context.Connection.ConnectionId,
            new TitleBookMutationResponse(message.Store, replaceMirror ? 3 : message.Space, replaceMirror ? 13 : message.Slot, message.Category, message.Index, 0x0A), ct);
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (!message.Complete || (message.Space is not (0 or 3) && !replaceMirror) || message.Slot < 0 || message.Category is < 0 or > 4 || message.Index < 0
            || session is null || !users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user is null)
        { await Refuse(); return; }
        using var held = await gate.EnterAsync(session.CharacterId, ct).ConfigureAwait(false);
        TitleBookMutationResult result;
        try
        {
            var slot = mapping.ToBusinessSlot(new(replaceMirror ? InventoryContainerType.Main : (InventoryContainerType)message.Space, message.Slot));
            result = replaceMirror
                ? await commands.SendAsync(new EquipCarriedTitleReplacingMirrorCommand(user.OnlineUserId, session, slot, message.ItemId, message.Category, message.Index), ct).ConfigureAwait(false)
                : message.Store
                ? await commands.SendAsync(new StoreTitleCommand(user.OnlineUserId, session, slot, message.ItemId, message.Category, message.Index), ct).ConfigureAwait(false)
                : await commands.SendAsync(new WithdrawTitleCommand(user.OnlineUserId, session, slot, message.ItemId, message.Category, message.Index), ct).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
        {
            logger.LogWarning(failure, "Title book request refused character={CharacterId} store={Store}", session.CharacterId, message.Store);
            await Refuse(); return;
        }
        var actual = mapping.ToGameSlot(result.Slot);
        await sender.SendCmdAsync(context.Connection.ConnectionId, new TitleBookMutationResponse(message.Store,
            (int)actual.Space, actual.Index, result.Category, result.Index, result.Success ? null : (byte)0x0A), ct).ConfigureAwait(false);
    }
}

public sealed record QueryTitleBookRequest(GameMessageMetadata Metadata, ushort Owner, bool Complete) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameQueryTitleBook;
}
internal sealed class QueryTitleBookDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new QueryTitleBookRequest(metadata, body.Length == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(body) : ushort.MaxValue, body.Length == 2);
}
public sealed record QueryTitleBookCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    SelectedSessionIdentity Target) : ICommand<IReadOnlyList<CharacterTitleBookCategoryState>>, IOnlineUserCommand, IAccountCommand, ISelectedSessionCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class QueryTitleBookCommandHandler(ITitleBookDomainService titles, ISelectedSessionDomainService sessions)
    : ICommandHandler<QueryTitleBookCommand, IReadOnlyList<CharacterTitleBookCategoryState>>
{
    public async ValueTask<IReadOnlyList<CharacterTitleBookCategoryState>> HandleAsync(CommandExecutionContext context, QueryTitleBookCommand command, CancellationToken ct)
    {
        if (command.Target.ListenerPort != command.Session.ListenerPort || sessions.FindByCharacter(command.Target.CharacterId) != command.Target) return [];
        return await titles.GetAsync(command.Target.CharacterId, ct).ConfigureAwait(false);
    }
}
public sealed class QueryTitleBookHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ICommandBus commands, IGameMessageSender sender) : ClientMessageHandler<QueryTitleBookRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, QueryTitleBookRequest message, CancellationToken ct)
    {
        if (!message.Complete || message.Owner == ushort.MaxValue) return;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        var target = sessions.FindByWireUserId(message.Owner);
        if (session is null || target is null || target.ListenerPort != session.ListenerPort
            || !users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user is null) return;
        var rows = await commands.SendAsync(new QueryTitleBookCommand(user.OnlineUserId, session, target), ct).ConfigureAwait(false);
        if (sessions.FindByCharacter(target.CharacterId) != target || sessions.FindByConnection(session.ConnectionId) != session) return;
        await sender.SendExplicitAsync(session.ConnectionId, new([.. rows.Select(x => (IGameServerMessage)new TitleBookStateMessage(x with { InfoType = 1, OwnerId16 = target.WireUserId }))], []), ct).ConfigureAwait(false);
    }
}

internal sealed record TitleBookChanged(CharacterId Character, int? Category = null, TitleUnlocked? Unlock = null,
    BusinessSlot? ChangedSlot = null, InventoryContainerType? ChangedContainer = null, bool RefreshCarriedInventory = false,
    int? ChangedIndex = null, bool RefreshBook = false) : INotification<TitleBookChanged>;
internal sealed class TitleBookEventSubscriber(IEventService events, AchievementTitleProjectionGate gate)
    : IDomainEventSubscriber<TitleUnlocked>, IDomainEventSubscriber<TitleStored>, IDomainEventSubscriber<TitleWithdrawn>, IDomainEventSubscriber<ItemMirrorRemoved>, IDomainEventSubscriber<TitleAppearanceChanged>
{
    private async ValueTask ProjectAsync(TitleBookChanged notice, CancellationToken ct)
    {
        using var held = await gate.EnterAsync(notice.Character, ct).ConfigureAwait(false);
        await events.ProjectAsync(new NotificationAudience.TheseCharacters([notice.Character]), notice, ct).ConfigureAwait(false);
    }
    public ValueTask HandleAsync(DomainEventEnvelope<TitleUnlocked> message, CancellationToken ct) =>
        ProjectAsync(new(message.Payload.CharacterId, message.Payload.Category, message.Payload, ChangedIndex: message.Payload.Index), ct);
    public ValueTask HandleAsync(DomainEventEnvelope<TitleStored> message, CancellationToken ct) =>
        ProjectAsync(new(message.Payload.CharacterId, message.Payload.Category, ChangedSlot: message.Payload.Slot, ChangedIndex: message.Payload.Index, RefreshBook: message.Payload.Slot.Space == BusinessInventorySpace.WornEquipment), ct);
    public ValueTask HandleAsync(DomainEventEnvelope<TitleWithdrawn> message, CancellationToken ct) =>
        ProjectAsync(new(message.Payload.CharacterId, message.Payload.Category, ChangedIndex: message.Payload.Index, ChangedSlot: message.Payload.Slot, RefreshCarriedInventory: message.Payload.Slot.Space == BusinessInventorySpace.CarriedEquipment || message.Payload.CarriedInventoryChanged, RefreshBook: message.Payload.Slot.Space == BusinessInventorySpace.WornEquipment), ct);
    public ValueTask HandleAsync(DomainEventEnvelope<TitleAppearanceChanged> message, CancellationToken ct) =>
        ProjectAsync(new(message.Payload.CharacterId, ChangedContainer: InventoryContainerType.Equipment), ct);
    public ValueTask HandleAsync(DomainEventEnvelope<ItemMirrorRemoved> message, CancellationToken ct) =>
        // Dedicated book operations publish their own committed title event.
        message.Payload.RefreshCarriedInventory ? ProjectAsync(new(message.Payload.CharacterId, null, ChangedContainer: message.Payload.ContainerType,
            RefreshCarriedInventory: message.Payload.RefreshCarriedInventory, RefreshBook: message.Payload.RefreshCarriedInventory), ct)
            : ValueTask.CompletedTask;
}
internal sealed class TitleBookChangedHandler(ITitleBookDomainService titles, ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService views, IInventorySlotProjectionDomainService slots)
    : INotificationHandler<TitleBookChanged>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, TitleBookChanged notification, CancellationToken ct)
    {
        if (recipient.CharacterId != notification.Character) return [];
        var messages = new List<IServerNetworkMessage>();
        if (notification.Unlock is { } unlock) messages.Add(new TitleUnlockedMessage(unlock));
        // ACK completes before committed equipment and silent book snapshots are delivered.
        var changedType = notification.ChangedContainer ?? (notification.ChangedSlot is { } changed ? slots.Project(changed).Space : (InventoryContainerType?)null);
        if (changedType is { } type && sessions.FindByConnection(recipient.ConnectionId) is { } session
            && await views.TryLoadAsync(session, ct).ConfigureAwait(false) is { } current)
        {
            var types = notification.RefreshCarriedInventory ? new[] { type, InventoryContainerType.Main } : new[] { type };
            foreach (var containerType in types.Distinct())
            {
                var container = current.CharacterInventory.Containers.Single(x => x.ContainerType == containerType);
                if (containerType == InventoryContainerType.Equipment)
                    messages.InsertRange(0, CharacterUserStateProjection.Compose(current));
                else
                    messages.Add(new InventoryFullSnapshotMessage(container, current.CharacterInventory, current.AccountInventory, current.World.PreparedAt));
            }
        }
        if (notification.RefreshBook || changedType == InventoryContainerType.Equipment || notification.Category is not null)
        {
            var categories = await titles.GetAsync(notification.Character, ct).ConfigureAwait(false);
            foreach (var category in categories.Where(x => notification.RefreshBook || changedType == InventoryContainerType.Equipment
                || x.Category == notification.Category))
                messages.Add(new TitleBookStateMessage(category));
        }
        return messages;
    }
}
internal sealed record TitleUnlockedMessage(TitleUnlocked Unlock) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0168;
    internal override byte[] EncodeBody()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Unlock.AchievementId); writer.Write(Unlock.Category); writer.Write(Unlock.Index); writer.Write(Unlock.ItemId); writer.Write(checked((ushort)Unlock.Index));
        return stream.ToArray();
    }
}
