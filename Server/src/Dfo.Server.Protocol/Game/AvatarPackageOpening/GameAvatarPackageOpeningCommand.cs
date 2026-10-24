using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using System.Collections.Immutable;
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

using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Protocol.Game.AvatarPackageOpening;

public sealed record GameAvatarPackageOpeningCommand(NetworkDispatchContext Dispatch, AvatarPackageOpeningRequest Input) : IProtocolCommand<PackageOpeningResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameAvatarPackageOpeningCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    IAvatarPackageOpeningDomainService avatars, IPackageOpeningDomainService packages, TimeProvider clock, ICommandWorkUnits units, ICommandProjections projections,
    IGameCommandOutbox outbox, ILogger<GameAvatarPackageOpeningCommandHandler> logger) : ICommandHandler<GameAvatarPackageOpeningCommand, PackageOpeningResult>
{
    public async ValueTask<PackageOpeningResult> HandleAsync(CommandExecutionContext context, GameAvatarPackageOpeningCommand command, CancellationToken ct)
    {
        var request = context.Request ?? throw new InvalidOperationException("Opening requires its explicit request.");
        var message = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        PackageOpeningResult result;
        if (message.Input is null) result = new(OpeningRejection.InvalidRequest);
        else if (session is null || request is CommandRequestContext.ProtocolEffectRejected
            || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online) || online is null)
            result = new(OpeningRejection.NoSession);
        else
        {
            if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
                throw new InvalidOperationException("Opening requires its originally selected actor.");
            var outcome = await units.WriteResultAsync(context, new("AvatarPackageOpening", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                async (unit, token) =>
                {
                    var footprint = unit.Footprint;
                    try { return new ActionOutcome(message.Input.Choices.IsEmpty ? await packages.OpenAsync(session, new(OpeningWireSlots.FromMain(message.Input.SourceSlot), []), clock.GetUtcNow(), footprint, token).ConfigureAwait(false) : await avatars.OpenAsync(session, OpeningWireSlots.Business(message.Input), clock.GetUtcNow(), footprint, token).ConfigureAwait(false), null); }
                    catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
                    {
                        unit.RequireRollback(failure);
                        return new ActionOutcome(null, failure);
                    }
                }, ct).ConfigureAwait(false);
            result = outcome switch
            {
                CommandWriteResult<ActionOutcome>.Returned { Value: { Failure: { } failure } } => Refused(failure, command.Dispatch),
                CommandWriteResult<ActionOutcome>.Returned { Value: { Result: { } value } } => value,
                CommandWriteResult<ActionOutcome>.CommitRolledBack rollback => Refused(rollback.Cause, command.Dispatch),
                _ => throw new InvalidOperationException("Opening returned no action outcome."),
            };
        }
        logger.LogInformation("AvatarPackageOpening connection={ConnectionId} slot={Slot} result={Result} detail={Detail}",
            command.Dispatch.Connection.ConnectionId, message.Input?.SourceSlot, result.Rejection, result.Detail);
        var response = new AvatarPackageOpeningResponse(message.Input?.SourceSlot ?? 0, result);
        if (result.Receipt is { } receipt)
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new OpeningCompleted(receipt, [response, new OpeningRewardPopup(message.Input!.SourceSlot, receipt.Rewards)]), ct).ConfigureAwait(false);
        else await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId, response, ct).ConfigureAwait(false);
        return result;
    }
    private PackageOpeningResult Refused(Exception failure, NetworkDispatchContext dispatch)
    {
        if (failure is OpeningRejectedException rejected) return new(rejected.Reason, Detail: rejected.Message);
        logger.LogError(failure, "Avatar package opening storage failure for {ConnectionId}", dispatch.Connection.ConnectionId);
        return new(OpeningRejection.StorageFailure);
    }
    private sealed record ActionOutcome(PackageOpeningResult? Result, Exception? Failure);
}
