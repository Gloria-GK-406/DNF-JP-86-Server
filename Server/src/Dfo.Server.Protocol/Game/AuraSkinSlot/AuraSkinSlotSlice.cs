using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.AuraSkinSlot;

/// <summary>CMD 0x0357 OPEN_AURA_SKIN_SLOT: the main-inventory slot holding the ticket, as an i32.</summary>
/// <remarks>A21 finds the ticket itself and writes its slot (VA 015A4AF3..015A4B0D).</remarks>
public sealed record AuraSkinSlotRequest(GameMessageMetadata Metadata, int SourceSlot, bool Complete)
    : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameOpenAuraSkinSlot;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.CharacterAppearance;
}

public sealed record AuraSkinSlotResponse(byte? ErrorCode = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameOpenAuraSkinSlot;

    /// <summary>A21 0x0357 handler (VA 011341A0) messages, from dstr.dat FILE/102.</summary>
    internal const byte Failed = 1;           // 0xEC56 光环幻化栏开启发生错误。
    internal const byte MissingTicket = 17;   // 0xEC54 开启光环幻化栏所需的材料不足。
    internal const byte AlreadyOpen = 18;     // 0xEC55 已经开启光环幻化栏。
}

internal sealed class AuraSkinSlotDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new AuraSkinSlotRequest(metadata, body.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(body) : -1, body.Length == 4);
}

internal static class AuraSkinSlotPayloadCodec
{
    // The handler only branches on the leading byte: 1 refreshes the avatar window, 0 shows the
    // message the second byte selects. The opened flag itself arrives in USERINFO subtype 1.
    public static byte[] Encode(AuraSkinSlotResponse response) =>
        response.ErrorCode is { } error ? [0, error] : [1];
}

internal sealed class AuraSkinSlotEncoder : GameCmdMessageEncoder<AuraSkinSlotResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AuraSkinSlotResponse message) =>
        OnePayload(message, AuraSkinSlotPayloadCodec.Encode(message));
}

public sealed record GameOpenAuraSkinSlotCommand(NetworkDispatchContext Dispatch, AuraSkinSlotRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameOpenAuraSkinSlotCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    IAuraSkinSlotDomainService slots, TimeProvider clock, ICommandWorkUnits units, ICommandProjections notifications, IGameCommandOutbox outbox,
    ISelectedCharacterCurrentViewDomainService views, ILogger<GameOpenAuraSkinSlotCommandHandler> logger)
    : ICommandHandler<GameOpenAuraSkinSlotCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameOpenAuraSkinSlotCommand command, CancellationToken ct)
    {
        var request = context.Request ?? throw new InvalidOperationException("Aura skin slot requires its explicit request.");
        var dispatch = command.Dispatch;
        var message = command.Input;
        if (!message.Complete || message.SourceSlot is < 0 or > short.MaxValue)
        {
            await RefuseAsync(dispatch, AuraSkinSlotResponse.Failed, ct).ConfigureAwait(false);
            return true;
        }
        var session = sessions.FindByConnection(dispatch.Connection.ConnectionId);
        if (session is null || !onlineUsers.TryGetByConnection(dispatch.Connection.ConnectionId, out var online) || online is null
            || request is CommandRequestContext.ProtocolEffectRejected)
        {
            await RefuseAsync(dispatch, AuraSkinSlotResponse.Failed, ct).ConfigureAwait(false);
            return true;
        }
        if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
            throw new InvalidOperationException("Aura skin slot requires its originally selected actor.");
        var outcome = await units.WriteResultAsync(context, new("AuraSkinSlot", [selected.Recipient], CommandWorkUnitKind.MainSettlement), async (unit, token) =>
        {
            try
            {
                return new ActionOutcome(await slots.OpenAsync(session, OpeningWireSlots.FromMain((short)message.SourceSlot),
                    clock.GetUtcNow(), unit.Footprint, token).ConfigureAwait(false), null);
            }
            catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
            {
                unit.RequireRollback(failure);
                return new ActionOutcome(null, failure);
            }
        }, ct).ConfigureAwait(false);
        Exception? error = outcome switch
        {
            CommandWriteResult<ActionOutcome>.Returned { Value: { Failure: { } failure } } => failure,
            CommandWriteResult<ActionOutcome>.CommitRolledBack rollback => rollback.Cause,
            _ => null,
        };
        if (error is not null)
        {
            byte code;
            if (error is OpeningRejectedException rejected)
            {
                code = rejected.Reason is OpeningRejection.WrongItem or OpeningRejection.MissingMaterial or OpeningRejection.InvalidRequest
                    ? AuraSkinSlotResponse.MissingTicket : AuraSkinSlotResponse.Failed;
                logger.LogInformation("Aura skin slot refused character={CharacterId} slot={Slot}: {Reason}", session.CharacterId, message.SourceSlot, error.Message);
            }
            else
            {
                code = AuraSkinSlotResponse.Failed;
                logger.LogError(error, "Aura skin slot transaction failed character={CharacterId}", session.CharacterId);
            }
            await RefuseAsync(dispatch, code, ct).ConfigureAwait(false);
            return true;
        }
        var result = outcome is CommandWriteResult<ActionOutcome>.Returned { Value: { Result: { } value } } ? value
            : throw new InvalidOperationException("Aura skin slot returned no action outcome.");
        if (result.Inventory is null)
        {
            logger.LogInformation("Aura skin slot already open character={CharacterId}; no ticket spent", session.CharacterId);
            await RefuseAsync(dispatch, AuraSkinSlotResponse.AlreadyOpen, ct).ConfigureAwait(false);
            return true;
        }
        var following = new List<IServerNetworkMessage>();
        try
        {
            var current = await views.TryLoadAsync(session, ct).ConfigureAwait(false);
            if (current is not null)
            {
                var equipment = current.CharacterInventory.Containers.Single(x => x.ContainerType == InventoryContainerType.Equipment).Items;
                following.Add(new ExtendedUserStateMessage(current.Character, current.CharacterEntry.Public,
                    current.CharacterEntry.Skills, equipment, result.Inventory.PreparedAt));
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
        {
            logger.LogError(failure, "Aura skin slot committed but the USERINFO projection failed character={CharacterId}", session.CharacterId);
        }
        logger.LogInformation("Aura skin slot opened character={CharacterId} ticketSlot={Slot}", session.CharacterId, message.SourceSlot);
        await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new OpeningCompleted(result.Inventory, [new AuraSkinSlotResponse()], FollowingMessages: following), ct).ConfigureAwait(false);
        return true;
    }

    private sealed record ActionOutcome(AuraSkinSlotResult? Result, Exception? Failure);

    private ValueTask RefuseAsync(NetworkDispatchContext dispatch, byte error, CancellationToken ct) =>
        outbox.RegisterCmdAsync(dispatch.Connection.ConnectionId, new AuraSkinSlotResponse(error), ct);
}

public sealed class AuraSkinSlotHandler(ICommandBus commands) : ClientMessageHandler<AuraSkinSlotRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameOpenAuraSkinSlotCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, AuraSkinSlotRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameOpenAuraSkinSlotCommand(context, message), ct).ConfigureAwait(false);
}
