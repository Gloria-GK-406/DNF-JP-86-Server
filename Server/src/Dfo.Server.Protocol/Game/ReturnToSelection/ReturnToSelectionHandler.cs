using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ReturnToSelection;

public sealed class ReturnToSelectionHandler(
    IOnlineUserQueryDomainService onlineUsers,
    OnlineUserExecutionGate userGate,
    SelectedSessionTransitionGate transitionGate,
    ICommandBus commands,
    CharacterSelectionDelivery delivery,
    INetworkConnectionCloser closer) : ClientMessageHandler<ReturnToSelectionRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        ReturnToSelectionRequest message, CancellationToken cancellationToken)
    {
        var connectionId = context.Connection.ConnectionId;
        if (!onlineUsers.TryGetByConnection(connectionId, out var online) || online?.AccountId is not { } accountId)
            throw new InvalidOperationException("Return-to-selection requires an authenticated connection.");
        ExceptionDispatchInfo? failure = null;
        // Same gate order as disconnect. Commands take only the account gate, never this user gate.
        await using (await userGate.AcquireAsync(online.OnlineUserId, UserAccessMode.Exclusive, cancellationToken)
                         .ConfigureAwait(false))
        await using (await transitionGate.AcquireAsync(accountId, UserAccessMode.Exclusive, cancellationToken)
                         .ConfigureAwait(false))
        {
            try
            {
                var current = onlineUsers.GetRequired(online.OnlineUserId);
                var prepared = await commands.SendAsync(new PrepareReturnToSelectionCommand(
                    accountId, connectionId, current.Generation), cancellationToken).ConfigureAwait(false);
                var completed = await commands.SendAsync(new CompleteReturnToSelectionCommand(prepared), cancellationToken)
                    .ConfigureAwait(false);
                if (completed.Notifications is { } plan)
                    await delivery.DeliverCleanupAsync(plan, cancellationToken).ConfigureAwait(false);
                await delivery.SendAsync(connectionId, new ReturnToSelectionResponse(),
                    prepared.Roster, prepared.ResetKnightDeck, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failure = ExceptionDispatchInfo.Capture(error);
                if (error is SelectedSessionCleanupFailure cleanupFailure)
                {
                    try
                    {
                        await delivery.DeliverCleanupAsync(cleanupFailure.NotificationPlan, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch { /* Keep the teardown failure as the primary cause. */ }
                }
            }
        }
        if (failure is not null)
        {
            // Closing invokes cleanup and must happen after releasing lifecycle ownership.
            // Never claim success after a save, teardown or partial-delivery failure.
            try { await closer.CloseAsync(connectionId, CancellationToken.None).ConfigureAwait(false); }
            catch { /* Preserve the original failure. */ }
            failure.Throw();
        }
    }
}
