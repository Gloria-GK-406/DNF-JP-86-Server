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
public sealed record IncreaseStatusCommand(SelectedSessionIdentity Session, BusinessSlot Slot)
    : ICommand<IncreaseStatusResult>, IRequestSettlementCommand
{
    public CharacterId CharacterId => Session.CharacterId;
}
public sealed class IncreaseStatusCommandHandler(ISelectedSessionDomainService sessions,
    IBusinessInventorySlotDomainService slots, IExperienceItemDomainService experience, TimeProvider clock)
    : ICommandHandler<IncreaseStatusCommand, IncreaseStatusResult>
{
    public async ValueTask<IncreaseStatusResult> HandleAsync(CommandExecutionContext context, IncreaseStatusCommand command, CancellationToken ct)
    {
        if (sessions.FindByCharacter(command.CharacterId) != command.Session)
            return new(ItemUsability.CharacterStateUnavailable);
        if (command.Slot.Space is not (BusinessInventorySpace.QuickSlot or BusinessInventorySpace.Consumable))
            return new(ItemUsability.NotAUsableItem);
        var slot = slots.Find(command.CharacterId, command.Slot);
        if (slot is null) return new(ItemUsability.CharacterStateUnavailable);
        var item = await slot.Container.GetAsync(slot.Slot.Index, ct).ConfigureAwait(false);
        if (item is null) return new(ItemUsability.NotAUsableItem, new(null, clock.GetUtcNow()));
        // This protocol cannot fall through to generic use: unknown stat items must not be consumed.
        var applied = await experience.TryUseAsync(command.Session, command.Slot, item, ct).ConfigureAwait(false);
        var current = await slot.Container.GetAsync(slot.Slot.Index, ct).ConfigureAwait(false);
        return new(applied?.Status ?? ItemUsability.NotAUsableItem, new(current?.Snapshot, clock.GetUtcNow()), applied);
    }
}
public sealed class IncreaseStatusHandler(ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService mappings, ICommandBus commands, IGameMessageSender sender,
    ILogger<IncreaseStatusHandler> logger) : ClientMessageHandler<IncreaseStatusRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, IncreaseStatusRequest request, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        IncreaseStatusResult result;
        try
        {
            result = session is null ? new(ItemUsability.CharacterStateUnavailable)
                : await commands.SendAsync(new IncreaseStatusCommand(session,
                    mappings.ToBusinessSlot(new(InventoryContainerType.Main, request.SourceSlot))), ct).ConfigureAwait(false);
        }
        catch (InventorySlotMappingException) { result = new(ItemUsability.NotAUsableItem); }
        catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
        {
            logger.LogError(error, "INCREASE_STATUS failed slot={Slot}", request.SourceSlot);
            result = new(ItemUsability.CharacterStateUnavailable);
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
        List<IGameServerMessage> messages = [new IncreaseStatusResponse(request.SourceSlot, code)];
        // Both success and refusal ACKs release the native pending operation; inventory follows ACK.
        if (result.Inventory is { } state)
            messages.Add(state.Item is { } item
                ? GameInventoryDelivery.Notification(new(InventoryContainerType.Main,
                    [new(item.ItemUid, request.SourceSlot, item.ToCore(), item.AvatarDetail)], state.At))
                : GameInventoryDelivery.ClearedSlotNotification(InventoryContainerType.Main, request.SourceSlot, state.At));
        if (result.Application?.ExperienceAward is { } award && result.Application.SkillPoints is { } points)
            messages.Add(GameExperienceDelivery.Notification(award, points));
        logger.LogInformation("INCREASE_STATUS character={Character} slot={Slot} status={Status}", session?.CharacterId, request.SourceSlot, result.Status);
        await sender.SendExplicitAsync(context.Connection.ConnectionId, new GameExplicitMessageDelivery(messages, []), ct).ConfigureAwait(false);
    }
}
