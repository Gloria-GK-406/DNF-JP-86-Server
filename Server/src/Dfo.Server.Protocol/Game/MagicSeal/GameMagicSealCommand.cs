using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MagicSeal;

public sealed record GameMagicSealCommand(NetworkDispatchContext Dispatch, MagicSealRequest Input) : IProtocolCommand<EquipmentMagicSealResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameMagicSealCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService mappings,
    IEquipmentMagicSealDomainService seals, ICommandWorkUnits units, ICommandProjections projections,
    IGameCommandOutbox outbox, ILogger<GameMagicSealCommandHandler> logger)
    : ICommandHandler<GameMagicSealCommand, EquipmentMagicSealResult>
{
    public async ValueTask<EquipmentMagicSealResult> HandleAsync(CommandExecutionContext context, GameMagicSealCommand command, CancellationToken ct)
    {
        var requestContext = context.Request ?? throw new InvalidOperationException("Equipment option requires its explicit request.");
        var request = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null || requestContext is CommandRequestContext.ProtocolEffectRejected)
        {
            logger.LogDebug("MAGIC_SEAL no selected session change={Change}", request.Change);
            await Reject(command.Dispatch, request, ct).ConfigureAwait(false);
            return new(EquipmentMagicSealRejection.NoSession);
        }
        if (request.TargetSlot < 0
            // A21 CMD 0x01B6 writes a reserved zero; unseal uses the -1 NPC sentinel.
            || request.MaterialSlot != (request.Change ? 0 : -1)
            || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            || online is null)
        {
            logger.LogWarning("MAGIC_SEAL invalid request change={Change} target={Target} second={Second}",
                request.Change, request.TargetSlot, request.MaterialSlot);
            await Reject(command.Dispatch, request, ct).ConfigureAwait(false);
            return new(EquipmentMagicSealRejection.InvalidRequest);
        }
        BusinessSlot target;
        try
        {
            target = mappings.ToBusinessSlot(
                new(InventoryContainerType.Main, request.TargetSlot));
        }
        catch (InventorySlotMappingException)
        {
            await Reject(command.Dispatch, request, ct).ConfigureAwait(false);
            return new(EquipmentMagicSealRejection.InvalidRequest);
        }
        if (requestContext is not CommandRequestContext.ProtocolEffectSelected selected || session != selected.Recipient.Session)
            throw new InvalidOperationException("MagicSeal requires its originally selected actor.");
        var result = await units.WriteAsync(context,
            new("magicseal", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            (unit, token) => request.Change
                ? seals.ChangeAsync(session, target, null, request.OptionIndex, unit.Footprint, token)
                : seals.UnsealAsync(session, target, null, unit.Footprint, token), ct).ConfigureAwait(false);
        if (result.Receipt is not { } receipt)
        {
            logger.LogDebug("MAGIC_SEAL rejected: {Reason}", result.Rejection);
            await Reject(command.Dispatch, request, ct, ErrorCode(result.Rejection))
                .ConfigureAwait(false);
            return result;
        }
        logger.LogInformation("MAGIC_SEAL character={Character} change={Change} slot={Slot} seed={Seed}",
            receipt.Session.CharacterId.Value, request.Change, request.TargetSlot, receipt.Seed);
        await projections.ProjectAsync(
            new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
            new MagicSealCompleted(receipt, request.Change), ct).ConfigureAwait(false);
        return result;
    }

    private static byte ErrorCode(EquipmentMagicSealRejection rejection) => rejection switch
    {
        EquipmentMagicSealRejection.InsufficientGold => 0x0A,
        EquipmentMagicSealRejection.MaterialUnavailable => 0x11,
        EquipmentMagicSealRejection.Locked => 0xD6,
        EquipmentMagicSealRejection.UnsupportedContent => 0x17,
        _ => 0x13,
    };

    private ValueTask Reject(NetworkDispatchContext context, MagicSealRequest request,
        CancellationToken ct, byte errorCode = 0x13) => request.Change
        ? outbox.RegisterCmdAsync(context.Connection.ConnectionId,
            new ChangeRandomOptionResponse(false, errorCode), ct)
        : ValueTask.CompletedTask;
}
