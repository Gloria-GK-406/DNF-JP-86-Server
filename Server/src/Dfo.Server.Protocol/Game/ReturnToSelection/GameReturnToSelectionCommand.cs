using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.AccountRoster;
using Dfo.Server.Protocol.Game.HonorLevel;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.ReturnToSelection;

public sealed record GameReturnToSelectionCommand(NetworkDispatchContext Dispatch, ReturnToSelectionRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal class GameReturnToSelectionCommandHandler(IOnlineUserQueryDomainService users,
    ISelectedSessionDomainService sessions, ICharacterDomainService rosterCharacters,
    ICommandWorkUnits units, ICommandSelectedSessionCleanup cleanup, IGameCommandOutbox outbox,
    ICommandProjections projections, ICommandConnectionClosures closures, IPartyService parties,
    IPartyProjectionStateDomainService projectionState, ILogger<GameReturnToSelectionCommandHandler> logger,
    IOutboundOrdering? ordering = null) : PartyMutationProtocolCommandHandler(sessions, parties, null,
        projections, projectionState.StreamOf, ordering, logger), ICommandHandler<GameReturnToSelectionCommand, bool>
{
    private readonly IOutboundOrdering? cleanupOrdering = ordering;
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameReturnToSelectionCommand command, CancellationToken ct)
    {
        var context = command.Dispatch;
        var connectionId = context.Connection.ConnectionId;
        if (!users.TryGetByConnection(connectionId, out var online) || online?.AccountId is not { } accountId)
            throw new InvalidOperationException("Return-to-selection requires an authenticated connection.");
        ExceptionDispatchInfo? failure = null;
        try
        {
            var prepared = await PrepareAsync(execution, accountId, connectionId, online.Generation, ct).ConfigureAwait(false);
            var completed = await CompleteAsync(execution, prepared, ct).ConfigureAwait(false);
            if (completed is { } plan) await DeliverCleanupAsync(plan, ct).ConfigureAwait(false);
            if (prepared.ResetKnightDeck)
                await outbox.RegisterNotiAsync(connectionId, new KnightShieldDeckMessage([0,0,0,0,0]), ct).ConfigureAwait(false);
            await outbox.RegisterCmdAsync(connectionId, new ReturnToSelectionResponse(), ct).ConfigureAwait(false);
            await outbox.RegisterNotiAsync(connectionId, new AccountCharacterRosterResponse(prepared.Roster, AnswersRosterQuery: false), ct).ConfigureAwait(false);
            await outbox.RegisterNotiAsync(connectionId, new HonorLevelInfoNotice(Dfo.Server.Data.Characters.AccountHonorLevel.Initial), ct).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = ExceptionDispatchInfo.Capture(error);
            if (error is SelectedSessionCleanupFailure cleanupFailure)
            {
                try { await DeliverCleanupAsync(cleanupFailure.NotificationPlan, CancellationToken.None).ConfigureAwait(false); }
                catch { /* Keep the teardown failure primary. */ }
            }
        }
        if (failure is not null)
        {
            await closures.RequestCloseAsync(context.Connection, CancellationToken.None).ConfigureAwait(false);
            failure.Throw();
        }
        return true;
    }
    protected virtual ValueTask<PreparedReturnToSelection> PrepareAsync(CommandExecutionContext execution,
        Dfo.Server.Data.Accounts.AccountId accountId, NetworkConnectionId connectionId, long generation, CancellationToken ct) =>
        units.WriteAsync(execution, new("PrepareReturnToSelection", []), (_, token) =>
            PrepareCoreAsync(accountId, connectionId, generation, token), ct);
    internal async ValueTask<PreparedReturnToSelection> PrepareCoreAsync(Dfo.Server.Data.Accounts.AccountId accountId,
        NetworkConnectionId connectionId, long generation, CancellationToken token)
    {
            ReturnToSelectionOwnership.Require(users, sessions, accountId, connectionId, generation);
            var session = sessions.Find(accountId);
            var roster = await rosterCharacters.GetAccountRosterWithAppearanceAsync(accountId, token).ConfigureAwait(false);
            var reset = session is not null && roster.Characters.Any(character => character.CharacterId == session.CharacterId && character.Job == 12);
            return new PreparedReturnToSelection(accountId, connectionId, generation, session, roster, reset);
    }
    protected internal virtual async ValueTask<SelectedSessionCleanupNotificationPlan?> CompleteAsync(CommandExecutionContext execution,
        PreparedReturnToSelection prepared, CancellationToken ct)
    {
        ReturnToSelectionOwnership.Require(users, sessions, prepared.AccountId, prepared.ConnectionId, prepared.Generation);
        if (prepared.Session is { } held ? sessions.StandingOf(held) is null : sessions.Find(prepared.AccountId) is not null)
            throw new InvalidOperationException("Return-to-selection session changed after preparation.");
        if (prepared.Session is not { } session) return null;
        return await cleanup.CleanupAsync(execution, session, includeOwner: false, cancellationToken: ct).ConfigureAwait(false);
    }
    protected virtual async ValueTask DeliverCleanupAsync(SelectedSessionCleanupNotificationPlan plan, CancellationToken ct)
    {
        using var order = cleanupOrdering?.Begin("XO-5 session cleanup", plan.RequiredOwnerNotices.Count > 0 ? OutboundDelivery.Required : OutboundDelivery.BestEffort);
        if (plan.RequiredOwnerNotices.Count > 0)
            await outbox.RegisterExplicitAsync(plan.OwnerConnectionId, new([.. plan.RequiredOwnerNotices.Select(CleanupNoticeMessages.ToMessage)], []), ct).ConfigureAwait(false);
        if (order is not null) order.Delivery = OutboundDelivery.BestEffort;
        if (plan.PeerAudience.Count > 0 && plan.PeerNotices.Count > 0)
            await projections.ProjectAsync(new NotificationAudience.TheseSessions(plan.PeerAudience), new SessionCleanupAnnounced(plan.PeerNotices), ct).ConfigureAwait(false);
        await DeliverPartyNotificationsAsync(plan.PartyNotifications, plan.PartyRecipients, ct).ConfigureAwait(false);
    }
}
