using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.EpicCraft;

public sealed record EpicCraftRequest(GameMessageMetadata Metadata, int EquipmentId, bool Valid = true) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameEpicCraft;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.EpicBook;
}
internal sealed class EpicCraftDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 4 ? new EpicCraftRequest(metadata, BinaryPrimitives.ReadInt32LittleEndian(body))
            : new EpicCraftRequest(metadata, 0, false);
}
public sealed record EpicCraftResponse(bool Success, byte Error = 2) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEpicCraft;
}
internal sealed class EpicCraftEncoder : GameCmdMessageEncoder<EpicCraftResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(EpicCraftResponse message) =>
        OnePayload(0x0313, message.Success ? new byte[] { 1 } : new byte[] { 0, message.Error });
}
public sealed record EpicCraftCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, int EquipmentId)
    : ICommand<EpicCraftReceipt>, IRequestSettlementCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class EpicCraftCommandHandler(IEpicCraftDomainService crafting, TimeProvider clock)
    : ICommandHandler<EpicCraftCommand, EpicCraftReceipt>
{
    public ValueTask<EpicCraftReceipt> HandleAsync(CommandExecutionContext context, EpicCraftCommand command, CancellationToken ct) =>
        crafting.CraftAsync(command.Session, command.EquipmentId, clock.GetUtcNow(), context.LegacyFootprint, ct);
}
public sealed class EpicCraftHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ICommandBus commands, IEventService events, IGameMessageSender sender, ILogger<EpicCraftHandler> logger)
    : ClientMessageHandler<EpicCraftRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, EpicCraftRequest request, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !request.Valid || request.EquipmentId <= 0
            || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online) || online is null)
        { await Reject(2); return; }
        EpicCraftReceipt receipt;
        try
        {
            receipt = await commands.SendAsync(new EpicCraftCommand(online.OnlineUserId, session, request.EquipmentId), ct).ConfigureAwait(false);
        }
        catch (OpeningRejectedException failure) when (!TransactionOutcomeUncertainException.Contains(failure))
        {
            logger.LogInformation("Epic craft rejected character={Character} equipment={Equipment} reason={Reason}",
                session.CharacterId, request.EquipmentId, failure.Reason);
            await Reject(failure.Reason switch { OpeningRejection.MissingMaterial => 0x16, OpeningRejection.NoRoom => 4, _ => 2 });
            return;
        }
        catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
        {
            logger.LogError(failure, "Epic craft transaction failed character={Character}", session.CharacterId);
            await Reject(2); return;
        }
        logger.LogInformation("Epic craft committed character={Character} equipment={Equipment} piece={Piece} balance={Balance}",
            session.CharacterId, request.EquipmentId, receipt.Pieces.PieceId, receipt.Pieces.Count);
        await events.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]), new EpicCraftCompleted(receipt), ct).ConfigureAwait(false);
        ValueTask Reject(byte error) => sender.SendCmdAsync(context.Connection.ConnectionId, new EpicCraftResponse(false, error), ct);
    }
}
public sealed record EpicCraftCompleted(EpicCraftReceipt Receipt) : INotification<EpicCraftCompleted>;
public sealed class EpicCraftCompletedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<EpicCraftCompleted>
{
    private static readonly IInventorySlotMappingRuleService Mappings = new InventorySlotMappingRuleService();
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, EpicCraftCompleted notification, CancellationToken ct)
    {
        var receipt = notification.Receipt.Inventory;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || !sessions.IsInWorld(receipt.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var messages = new List<IServerNetworkMessage> { new EpicCraftResponse(true), new EpicBookStateMessage(1, [notification.Receipt.Pieces]) };
        var updates = receipt.Updates.GroupBy(x => OpeningWireSlots.Project(x.Slot, Mappings).Space)
            .Select(group => new CommittedInventoryUpdateState(group.Key, group.Select(x => new InventoryItemState(x.Identity,
                OpeningWireSlots.Project(x.Slot, Mappings).Index, x.Core.ToCore())).ToArray(), receipt.PreparedAt)).ToArray();
        messages.AddRange(GameInventoryDelivery.Notifications(updates));
        // Unlike recipe/opening ACKs, CMD0313 carries no cost list: exhausted account materials also need explicit removal.
        foreach (var slot in receipt.Cleared)
        {
            var wire = OpeningWireSlots.Project(slot, Mappings);
            messages.Add(GameInventoryDelivery.ClearedSlotNotification(wire.Space, wire.Index, receipt.PreparedAt));
        }
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(messages);
    }
}
