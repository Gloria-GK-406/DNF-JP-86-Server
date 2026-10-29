using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.MarriageProposal;
public sealed record AdvanceWeddingTickCommand : ICommand<bool>;
internal sealed partial class GameMarriageCommandHandler
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, AdvanceWeddingTickCommand command, CancellationToken token)
    {
        foreach (var ceremony in await marriage.ActiveWeddingsAsync(token).ConfigureAwait(false))
        {
            Exception? rejected = null;
            var advanceWrite = await units.WriteResultAsync(context, new("wedding.advance", []), async (unit, ct) =>
            {
                try { return new MarriageWeddingAdvanceResult(await venue.AdvanceAsync(ceremony, ct).ConfigureAwait(false)); }
                catch (Exception error) when (IsWeddingBusinessFailure(error))
                { rejected = error; unit.RequireRollback(error); return new MarriageWeddingAdvanceResult(null); }
            }, token).ConfigureAwait(false);
            if (advanceWrite is CommandWriteResult<MarriageWeddingAdvanceResult>.CommitRolledBack rolledBack) rejected = rolledBack.Cause;
            if (rejected is not null) { logger.LogError(rejected, "Wedding advancement failed for {Ceremony}", ceremony); continue; }
            var advance = ((CommandWriteResult<MarriageWeddingAdvanceResult>.Returned)advanceWrite).Value;
            if (advance.Transition is not {} result) continue;
            logger.LogInformation("Wedding {Ceremony} reached {Phase}", ceremony, result.Wedding.Phase);
            var completed = result.Wedding.Phase == MarriageWeddingPhase.Completed;
            var ceremonyFailed = false;
            using (timerOrdering?.Begin("XO-7 wedding positions before the cinema", OutboundDelivery.BestEffort))
            {
                foreach (var standing in result.Standing)
                    await WeddingPositionAsync(context, standing, token).ConfigureAwait(false);
                foreach (var participant in result.Participants)
                {
                    MarriageEngagementSnapshot? snapshot = null;
                    try { snapshot = await marriage.SnapshotAsync(participant.Actor.AccountId, participant.Actor.CharacterId, token).ConfigureAwait(false); }
                    catch (Exception error) when (IsWeddingBusinessFailure(error))
                    { context.Request!.DemandCurrent(); logger.LogError(error, "Wedding advancement failed for {Ceremony}", ceremony); ceremonyFailed = true; }
                    if (ceremonyFailed) break;
                    var messages = new List<IGameServerMessage>();
                    if (completed) messages.Add(new WeddingBeginNotification(participant.Church));
                    messages.Add(new WeddingInfoMessage(snapshot!));
                    if (!completed) messages.Add(new GameServerNoticeMessage("婚礼因双方未能按时到场而取消，费用已通过邮件退回。"));
                    await outbox.RegisterExplicitAsync(participant.Actor.ConnectionId, new(messages, []), token).ConfigureAwait(false);
                }
            }
            if (ceremonyFailed) continue;
            foreach (var guest in result.Guests)
                if (completed) await outbox.RegisterNotiAsync(guest.Actor.ConnectionId, new WeddingBeginNotification(guest.Church), token).ConfigureAwait(false);
            foreach (var departure in result.Departures)
                await WeddingMoveAsync(context, departure.Actor, departure.Channel, departure.Location, token).ConfigureAwait(false);
            foreach (var cleanup in result.Cleanup)
            {
                await AppearanceAsync(cleanup.Actor, cleanup.Church, true, token).ConfigureAwait(false);
                Exception? cleanupRejected = null;
                var cleanupWrite = await units.WriteResultAsync(context, new("wedding.cleanup", TimerRecipientCapture.Capture([cleanup.Actor], users)), async (unit, ct) =>
                {
                    try { await venue.CompleteCleanupAsync(ceremony, cleanup.Actor.CharacterId, ct).ConfigureAwait(false); return true; }
                    catch (Exception error) when (IsWeddingBusinessFailure(error))
                    { cleanupRejected = error; unit.RequireRollback(error); return false; }
                }, token).ConfigureAwait(false);
                if (cleanupWrite is CommandWriteResult<bool>.CommitRolledBack cleanupRollback) cleanupRejected = cleanupRollback.Cause;
                if (cleanupRejected is not null) logger.LogWarning(cleanupRejected, "Committed wedding projection failed for one recipient; other recipients continue.");
            }
        }
        return true;
    }
    private async ValueTask WeddingMoveAsync(CommandExecutionContext context, SelectedSessionIdentity actor, int channel,
        MarriageChurchLocation location, CancellationToken token)
    {
        Dfo.Server.Data.Game.SelectCharacterResult view;
        Dfo.Server.Abstractions.World.TownAreaMember previous;
        try
        {
            if (!sessions.IsInWorld(actor) || onlineCharacters.StandingOf(actor) is not {} observed
                || await currentViews.TryLoadAsync(actor, token).ConfigureAwait(false) is not {} currentView) return;
            previous = observed; view = currentView;
            var instance = CoupleRoomTown.Is(location.TownId) ? await housing.InstanceAsync(actor, token).ConfigureAwait(false) : 0;
            if (CoupleRoomTown.Is(location.TownId) && instance is not > 0) return;
            if (onlineCharacters.CrossInto(actor, new(channel, location.TownId, location.AreaId, instance ?? 0), location.X, location.Y, true) is null) return;
        }
        catch (Exception error) when (IsWeddingBusinessFailure(error))
        { context.Request!.DemandCurrent(); logger.LogWarning(error, "Committed wedding projection failed for one recipient; other recipients continue."); return; }
        await TownMovementProjection.TownProjectTransferAsync(sessions, townAreas, projections, new PartyTownProjectionDelivery(parties, townAreas, sessions, projections, projectionState), currentViews, housing, outbox, housingSessions, actor.ConnectionId, actor, view, previous, token).ConfigureAwait(false);
    }
    private static bool IsWeddingBusinessFailure(Exception error) => error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error);
    private async ValueTask WeddingPositionAsync(CommandExecutionContext context, MarriageWeddingStanding standing, CancellationToken token)
    {
        var actor = standing.Actor;
        if (!sessions.IsInWorld(actor) || onlineCharacters.StandingOf(actor) is not {} area
            || area.Area.TownId != standing.Position.TownId || area.Area.AreaId != standing.Position.AreaId) return;
        Dfo.Server.Abstractions.World.TownAreaMovement? movement;
        try
        {
            movement = onlineCharacters.StepTo(actor, standing.Position.X, standing.Position.Y, standing.Position.Direction);
        }
        catch (Exception error) when (IsWeddingBusinessFailure(error))
        { context.Request!.DemandCurrent(); logger.LogWarning(error, "Committed wedding projection failed for one recipient; other recipients continue."); return; }
        if (movement is null) return;
        var own = movement.Area.Members.Single(member => member.Session == actor);
        var message = new ForcedTownPositionNotification(actor.WireUserId, own.Position.X, own.Position.Y, own.Position.Direction);
        var targets = own.SharesTownPresence ? movement.Area.Members.Where(member => member.SharesTownPresence && member.CharacterId != actor.CharacterId)
            .Select((member => TownArrivalProjection.TownExactSession(sessions, member))).Where(candidate => candidate is not null).Select(candidate => new GameExplicitMessageTarget(candidate!.ConnectionId, [message])).ToArray() : [];
        await outbox.RegisterExplicitAsync(actor.ConnectionId, new([message], targets), token).ConfigureAwait(false);
    }
}
