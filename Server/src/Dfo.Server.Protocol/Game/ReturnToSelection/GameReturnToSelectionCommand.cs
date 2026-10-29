using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Collections.Immutable;
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
internal sealed partial class GameReturnToSelectionCommandHandler(IOnlineUserQueryDomainService users,
    ISelectedSessionDomainService sessions, ICharacterRosterDomainService rosterCharacters,
    ICommandWorkUnits units, ICommandSelectedSessionCleanup cleanup, IGameCommandOutbox outbox,
    ICommandProjections projections, ICommandConnectionClosures closures, IPartyService parties,
    IPartyProjectionStateDomainService projectionState,
    IOutboundOrdering ordering) :  ICommandHandler<GameReturnToSelectionCommand, bool>
{



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
    private ValueTask<PreparedReturnToSelection> PrepareAsync(CommandExecutionContext execution,
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
    internal async ValueTask<SelectedSessionCleanupNotificationPlan?> CompleteAsync(CommandExecutionContext execution,
        PreparedReturnToSelection prepared, CancellationToken ct)
    {
        ReturnToSelectionOwnership.Require(users, sessions, prepared.AccountId, prepared.ConnectionId, prepared.Generation);
        if (prepared.Session is { } held ? sessions.StandingOf(held) is null : sessions.Find(prepared.AccountId) is not null)
            throw new InvalidOperationException("Return-to-selection session changed after preparation.");
        if (prepared.Session is not { } session) return null;
        return await cleanup.CleanupAsync(execution, session, includeOwner: false, cancellationToken: ct).ConfigureAwait(false);
    }
    private async ValueTask DeliverCleanupAsync(SelectedSessionCleanupNotificationPlan plan, CancellationToken ct)
    {
        using var order = ordering.Begin("XO-5 session cleanup", plan.RequiredOwnerNotices.Count > 0 ? OutboundDelivery.Required : OutboundDelivery.BestEffort);
        if (plan.RequiredOwnerNotices.Count > 0)
            await outbox.RegisterExplicitAsync(plan.OwnerConnectionId, new([.. plan.RequiredOwnerNotices.Select(CleanupNoticeMessages.ToMessage)], []), ct).ConfigureAwait(false);
        order.Delivery = OutboundDelivery.BestEffort;
        if (plan.PeerAudience.Count > 0 && plan.PeerNotices.Count > 0)
            await projections.ProjectAsync(new NotificationAudience.TheseSessions(plan.PeerAudience), new SessionCleanupAnnounced(plan.PeerNotices), ct).ConfigureAwait(false);
        await PartyNotificationStages.DeliverPartyNotificationsAsync(projections, projectionState, parties, ordering, plan.PartyNotifications, plan.PartyRecipients, ct).ConfigureAwait(false);
    }
}
