using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CompoundItem;

public sealed record CompoundItemRequest(GameMessageMetadata Metadata, int Source, byte SourceMode, ushort Count,
    bool Valid = true) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => ClientMessageType.GameCompoundItem;
}
internal sealed class CompoundItemDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length is 7 or 9 ? new CompoundItemRequest(metadata, BinaryPrimitives.ReadInt32LittleEndian(body),
            body[4], BinaryPrimitives.ReadUInt16LittleEndian(body[5..]), body.Length == 7 || body[7] == 0 && body[8] == 1)
        : new CompoundItemRequest(metadata, -1, 0, 0, false);
}
public sealed record CompoundItemResponse(RecipeCraftReceipt? Receipt, byte Error = 0x11) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCompoundItem;
}
internal sealed class CompoundItemEncoder : GameCmdMessageEncoder<CompoundItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CompoundItemResponse message)
    {
        if (message.Receipt is not { } receipt) return OnePayload(0x0019, new byte[] { 0, message.Error });
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write((byte)1); w.Write(checked((byte)(receipt.Consumed.Length)));
        foreach (var cost in receipt.Consumed)
        {
            w.Write((byte)0); w.Write(checked((ushort)OpeningWireSlots.RewardIndex(cost.Slot))); w.Write(cost.Count);
        }
        w.Write(checked((byte)receipt.Inventory.Rewards.Length));
        foreach (var reward in receipt.Inventory.Rewards)
        {
            var core = receipt.Inventory.Updates.Single(x => x.Slot == reward.Slot).Core.ToCore();
            w.Write(core.ItemKind == 1 ? (byte)1 : (byte)0);
            w.Write(checked((ushort)OpeningWireSlots.RewardIndex(reward.Slot))); w.Write(reward.ItemId);
            w.Write(core.ItemKind == 1 ? core.Value : reward.Count);
            w.Write(core.Attribute); w.Write(core.Durability); w.Write(core.SealFlag);
            w.Write(core.AmplifyValue); w.Write(core.AmplifyType); w.Write(core.Marker16 == -1 ? 0 : core.Marker16);
            w.Write(core.GenuineUpgrade); w.Write(core.EmancipateEquipmentLevel); w.Write(core.TradeRestriction);
            w.Write(core.TailUnknown0); w.Write(core.TailUnknown1); w.Write(core.TailUnknown2);
            w.Write(core.TailUnknown3); w.Write(core.RemainUseCount); w.Write(core.SortLockFlag);
            // A21 handler D36DB7/D36DD1 initializes two opaque values (4/8 bytes), then reads the final byte.
            // Keep these fields on each reward row, including multi-output recipes, rather than once at packet end.
            w.Write(new byte[12]); w.Write(core.EquipmentLockId);
        }
        return OnePayload(0x0019, stream.ToArray());
    }
}
public sealed record CompoundItemCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    BusinessSlot Drawing, int Count) : ICommand<RecipeCraftReceipt>, IRequestSettlementCommand, IAccountCommand
{ public AccountId AccountId => Session.AccountId; }
public sealed class CompoundItemCommandHandler(IRecipeCraftDomainService recipes, TimeProvider clock)
    : ICommandHandler<CompoundItemCommand, RecipeCraftReceipt>
{
    public ValueTask<RecipeCraftReceipt> HandleAsync(CommandExecutionContext context, CompoundItemCommand command,
        CancellationToken ct) => recipes.CraftAsync(command.Session, command.Drawing, command.Count, clock.GetUtcNow(), context.LegacyFootprint, ct);
}
public sealed class CompoundItemHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ICommandBus commands, IEventService notifications, IGameMessageSender sender,
    ILogger<CompoundItemHandler> logger) : ClientMessageHandler<CompoundItemRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, CompoundItemRequest request, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null) { await Reject(0x11); return; }
        if (!request.Valid || request.SourceMode != 0
            || request.Count is < 1 or > 1000 || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null) { await Reject(0x11); return; }
        RecipeCraftReceipt receipt;
        try
        {
            receipt = await commands.SendAsync(new CompoundItemCommand(online.OnlineUserId, session,
                OpeningWireSlots.FromMain(request.Source), request.Count), ct).ConfigureAwait(false);
        }
        catch (OpeningRejectedException failure) when (!Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure))
        {
            var error = OpeningPayloads.Error(failure.Reason);
            logger.LogInformation("Compound rejected: {Reason} {Detail}", failure.Reason, failure.Message);
            await Reject(error); return;
        }
        catch (Exception failure) when ((failure is not OperationCanceledException) && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure))
        {
            logger.LogError(failure, "Compound transaction failed for {Character}", session.CharacterId);
            await Reject(0x11); return;
        }
        logger.LogInformation("Compound committed character={Character} drawingSlot={Slot} count={Count}", session.CharacterId, request.Source, request.Count);
        await Publish(receipt, true);
        ValueTask Reject(byte error) => sender.SendCmdAsync(context.Connection.ConnectionId, new CompoundItemResponse(null, error), ct);
        async ValueTask Publish(RecipeCraftReceipt result, bool acknowledge)
        {
            // First delivery preserves the existing acknowledgement and committed inventory notifications.
            await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
                new OpeningCompleted(result.Inventory, acknowledge ? [new CompoundItemResponse(result)] : []), ct).ConfigureAwait(false);
        }
    }
}
