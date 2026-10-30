using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.IncreaseStatus;

public sealed record IncreaseStatusRequest(GameMessageMetadata Metadata, short SourceSlot) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameIncreaseStatus;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.Experience;
}
internal sealed class IncreaseStatusDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 2 || BinaryPrimitives.ReadInt16LittleEndian(body) < 0)
            throw new InvalidDataException("INCREASE_STATUS requires one nonnegative i16 source slot.");
        return new IncreaseStatusRequest(metadata, BinaryPrimitives.ReadInt16LittleEndian(body));
    }
}
public sealed record IncreaseStatusResponse(short SourceSlot, byte? Error = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameIncreaseStatus;
}
internal sealed class IncreaseStatusEncoder : GameCmdMessageEncoder<IncreaseStatusResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(IncreaseStatusResponse message)
    {
        if (message.Error is { } error) return OnePayload(0x001E, new byte[] { 0, error });
        // A21 VA0112E00A..42: source i16, status type u8, value i32, two u16 fields.
        // FF declares no direct stat mutation: authoritative EXP is sent separately as NOTI0025.
        var body = new byte[12];
        body[0] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), message.SourceSlot);
        body[3] = 0xFF;
        return OnePayload(0x001E, body);
    }
}
public sealed record IncreaseStatusSlotState(ItemSnapshot? Item, DateTimeOffset At);
public sealed record IncreaseStatusResult(ItemUsability Status, IncreaseStatusSlotState? Inventory = null, ItemUseApplication? Application = null);
public sealed record GameIncreaseStatusCommand(NetworkDispatchContext Dispatch, IncreaseStatusRequest Input) : IProtocolCommand<IncreaseStatusResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameIncreaseStatusCommandHandler(ISelectedSessionDomainService sessions,
    IBusinessInventorySlotDomainService slots,IExperienceItemDomainService experience,IReviveCoinDomainService reviveCoins,TimeProvider clock,
    ICommandWorkUnits units,IGameCommandOutbox outbox,ILogger<GameIncreaseStatusCommandHandler> logger) : ICommandHandler<GameIncreaseStatusCommand,IncreaseStatusResult>
{
    public async ValueTask<IncreaseStatusResult> HandleAsync(CommandExecutionContext context,GameIncreaseStatusCommand command,CancellationToken ct)
    {
        var request=context.Request ?? throw new InvalidOperationException("Increase status requires its explicit request.");
        var message=command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        IncreaseStatusResult result;
        if (session is null || request is CommandRequestContext.ProtocolEffectRejected)
            result = new(ItemUsability.CharacterStateUnavailable);
        else
        {
            if(request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
                throw new InvalidOperationException("Increase status requires its originally selected actor.");
            BusinessSlot? slot = null;
            IncreaseStatusResult? mappingFailure=null;
            try { slot = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, message.SourceSlot)); }
            catch (InventorySlotMappingException) { }
            catch(Exception error) when(error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
            { mappingFailure=Refused(error,message.SourceSlot); }
            if(slot is null) result = mappingFailure ?? new(ItemUsability.NotAUsableItem);
            else
            {
                var outcome = await units.WriteResultAsync(context,new("IncreaseStatus",[selected.Recipient],CommandWorkUnitKind.MainSettlement),async (unit,token) =>
                {
                    var footprint=unit.Footprint;
                    try { return new ActionOutcome(await ApplyAsync(session,slot.Value,footprint,token).ConfigureAwait(false),null); }
                    catch(Exception error) when(error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
                    { unit.RequireRollback(error); return new ActionOutcome(null,error); }
                },ct).ConfigureAwait(false);
                result=outcome switch {
                    CommandWriteResult<ActionOutcome>.Returned {Value:{Failure:{} error}} => Refused(error,message.SourceSlot),
                    CommandWriteResult<ActionOutcome>.Returned {Value:{Result:{} value}} => value,
                    CommandWriteResult<ActionOutcome>.CommitRolledBack rollback => Refused(rollback.Cause,message.SourceSlot),
                    _ => throw new InvalidOperationException("Increase status returned no action outcome."),
                };
            }
        }
        byte? code = result.Status switch
        {
            ItemUsability.Allowed => null,
            ItemUsability.LevelTooLow => 14,
            ItemUsability.JobMismatch => 8,
            ItemUsability.OnCooldown or ItemUsability.PlaceNotAllowed => 19,
            ItemUsability.NotAUsableItem => 23,
            _ => 2,
        };
        List<IGameServerMessage> messages = [new IncreaseStatusResponse(message.SourceSlot, code)];
        // Both success and refusal ACKs release the native pending operation; inventory follows ACK.
        if (result.Inventory is { } state)
            messages.Add(state.Item is { } item
                ? GameInventoryDelivery.Notification(new(InventoryContainerType.Main,
                    [new(item.ItemUid, message.SourceSlot, item.ToCore(), item.AvatarDetail)], state.At))
                : GameInventoryDelivery.ClearedSlotNotification(InventoryContainerType.Main, message.SourceSlot, state.At));
        if (result.Application?.ExperienceAward is { } award && result.Application.SkillPoints is { } points)
            messages.Add(GameExperienceDelivery.Notification(award, points));
        if (result.Application?.OpeningReceipt is { } receipt)
            foreach (var update in receipt.Updates.Where(x => x.Slot.Space == BusinessInventorySpace.VirtualResource))
                messages.Add(GameInventoryDelivery.Notification(new(InventoryContainerType.Main,
                    [new(update.Identity, update.Slot.Index, update.Core.ToCore(), update.Core.AvatarDetail)], receipt.PreparedAt)));
        logger.LogInformation("INCREASE_STATUS character={Character} slot={Slot} status={Status}", session?.CharacterId, message.SourceSlot, result.Status);
        await outbox.RegisterExplicitAsync(command.Dispatch.Connection.ConnectionId, new GameExplicitMessageDelivery(messages, []), ct).ConfigureAwait(false);
        return result;
    }
    private async ValueTask<IncreaseStatusResult> ApplyAsync(SelectedSessionIdentity session,BusinessSlot businessSlot,IEffectFootprintRecorder footprint,CancellationToken ct)
    {
        if (!sessions.IsInWorld(session))
            return new(ItemUsability.CharacterStateUnavailable);
        if (businessSlot.Space is not (BusinessInventorySpace.QuickSlot or BusinessInventorySpace.Consumable))
            return new(ItemUsability.NotAUsableItem);
        var slot = slots.Find(session.CharacterId, businessSlot);
        if (slot is null) return new(ItemUsability.CharacterStateUnavailable);
        var item = await slot.Container.GetAsync(slot.Slot.Index, ct).ConfigureAwait(false);
        if (item is null) return new(ItemUsability.NotAUsableItem, new(null, clock.GetUtcNow()));
        // Native A21 sends this entry for coin 42 as well as EXP books. Unknown stat items still
        // cannot fall through to generic consume-only use.
        var applied = await reviveCoins.TryUseAsync(session, businessSlot, item, footprint, ct).ConfigureAwait(false)
            ?? await experience.TryUseAsync(session, businessSlot, item, footprint, ct).ConfigureAwait(false);
        var current = await slot.Container.GetAsync(slot.Slot.Index, ct).ConfigureAwait(false);
        return new(applied?.Status ?? ItemUsability.NotAUsableItem, new(current?.Snapshot, clock.GetUtcNow()), applied);
    }
    private IncreaseStatusResult Refused(Exception error,short slot)
    {
        if(error is InventorySlotMappingException) return new(ItemUsability.NotAUsableItem);
        logger.LogError(error,"INCREASE_STATUS failed slot={Slot}",slot);
        return new(ItemUsability.CharacterStateUnavailable);
    }
    private sealed record ActionOutcome(IncreaseStatusResult? Result,Exception? Failure);
}
public sealed class IncreaseStatusHandler(ICommandBus commands) : ClientMessageHandler<IncreaseStatusRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameIncreaseStatusCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,IncreaseStatusRequest message,CancellationToken ct) =>
        _ = await commands.SendAsync(new GameIncreaseStatusCommand(context,message),ct).ConfigureAwait(false);
}
