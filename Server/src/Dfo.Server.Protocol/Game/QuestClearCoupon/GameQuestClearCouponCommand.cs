using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Fatigue;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.QuestClearCoupon;

public sealed record GameQuestClearCouponCommand(NetworkDispatchContext Dispatch, QuestClearCouponRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameQuestClearCouponCommandHandler(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    IBusinessInventorySlotDomainService slots, IEntryAllowanceItemDomainService entryAllowances,
    IOpeningInventoryDomainService inventory, IFatiguePotionDomainService potions,
    IQuestClearCouponSettlementDomainService coupons, ICharacterFatigueDomainService fatigue,
    ISelectedCharacterCurrentViewDomainService currentViews, ICharacterDomainService characters,
    ICharacterQuestLogDomainService questLogs, IQuestOfferRefreshDomainService offers,
    IItemUsageDomainService usage, TimeProvider clock,
    ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox,
    ILogger<GameQuestClearCouponCommandHandler> logger, IMirrorAradQuestDomainService? mirror = null)
    : ICommandHandler<GameQuestClearCouponCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameQuestClearCouponCommand command, CancellationToken ct)
    {
        var request = context.Request ?? throw new InvalidOperationException("Stackable action requires its explicit request.");
        var message = command.Input;
        var connection = command.Dispatch.Connection.ConnectionId;
        var complete = message.Complete && message.SourceSlot >= 0 && message.Container == 0 && message.Reserved1 == 0
            && message.Reserved2 == 0 && message.Reserved3 == 0 && message.Action <= byte.MaxValue;
        if (!complete) { await RefuseAsync(connection, message, true, ct).ConfigureAwait(false); return false; }
        var session = sessions.FindByConnection(connection);
        if (session is null || request is CommandRequestContext.ProtocolEffectRejected
            || !onlineUsers.TryGetByConnection(connection, out var online) || online is null)
        { await RefuseAsync(connection, message, false, ct).ConfigureAwait(false); return false; }
        if (!sessions.IsInWorld(session)) return false;
        if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
            throw new InvalidOperationException("Stackable action requires its originally selected actor.");

        Branch branch;
        try
        {
            var slot = OpeningWireSlots.FromMain(message.SourceSlot);
            var source = await slots.Get(session.CharacterId, slot).Container.GetAsync(slot.Index, ct).ConfigureAwait(false);
            branch = source is not null && entryAllowances.Supports(source.ItemId) ? Branch.Entry
                : message.IsFatiguePotion ? Branch.Fatigue : Branch.Coupon;
        }
        catch (Exception failure) when (CanRefuse(failure))
        {
            logger.LogWarning(failure, "Heras entry coupon refused character={CharacterId} slot={Slot}", session.CharacterId, message.SourceSlot);
            await RefuseAsync(connection, message, false, ct).ConfigureAwait(false); return false;
        }
        if (branch == Branch.Coupon && !message.IsSupported)
        { await RefuseAsync(connection, message, true, ct).ConfigureAwait(false); return false; }

        var outcome = await units.WriteResultAsync(context,
            new("QuestClearCoupon." + branch, [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                var footprint = unit.Footprint;
                try
                {
                    var slot = OpeningWireSlots.FromMain(message.SourceSlot);
                    if (branch == Branch.Entry)
                    {
                        var source = await inventory.SourceAsync(session, slot, clock.GetUtcNow(), token).ConfigureAwait(false);
                        var used = await entryAllowances.TryUseAsync(session, slot, source, footprint, token).ConfigureAwait(false);
                        return new ActionOutcome(used?.OpeningReceipt ?? throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Not a usable Heras entry coupon."), null, null);
                    }
                    if (branch == Branch.Fatigue)
                        return new ActionOutcome(await potions.UseAsync(session, slot, clock.GetUtcNow(), footprint, token).ConfigureAwait(false), null, null);
                    var result = await coupons.UseAsync(session, slot, clock.GetUtcNow(), footprint, token).ConfigureAwait(false);
                    return new ActionOutcome(result.Inventory, result, null);
                }
                catch (Exception failure) when (CanRefuse(failure))
                {
                    unit.RequireRollback(failure);
                    return new ActionOutcome(null, null, failure);
                }
            }, ct).ConfigureAwait(false);
        var action = outcome switch
        {
            CommandWriteResult<ActionOutcome>.Returned returned => returned.Value,
            CommandWriteResult<ActionOutcome>.CommitRolledBack rollback => new ActionOutcome(null, null, rollback.Cause),
            _ => throw new InvalidOperationException("Stackable action returned no outcome."),
        };
        if (action.Failure is { } actionFailure)
        {
            if (branch == Branch.Entry)
                logger.LogWarning(actionFailure, "Heras entry coupon refused character={CharacterId} slot={Slot}", session.CharacterId, message.SourceSlot);
            else if (branch == Branch.Fatigue)
            {
                if (actionFailure is OpeningRejectedException rejected)
                    logger.LogInformation("Fatigue potion refused character={CharacterId} slot={Slot}: {Reason}", session.CharacterId, message.SourceSlot, rejected.Message);
                else logger.LogError(actionFailure, "Fatigue potion failed character={CharacterId} slot={Slot}", session.CharacterId, message.SourceSlot);
            }
            else if (actionFailure is OpeningRejectedException rejected)
                logger.LogInformation("Quest clear coupon rejected character={CharacterId} slot={Slot}: {Reason}", session.CharacterId, message.SourceSlot, rejected.Reason);
            else logger.LogError(actionFailure, "Quest clear coupon failed character={CharacterId} slot={Slot}", session.CharacterId, message.SourceSlot);
            await RefuseAsync(connection, message, branch == Branch.Coupon, ct).ConfigureAwait(false);
            if (branch == Branch.Fatigue && actionFailure is OpeningRejectedException)
                await SendItemUsageAsync(context, session, ct).ConfigureAwait(false);
            return false;
        }
        var receipt = action.Inventory ?? throw new InvalidOperationException("Stackable action returned no inventory receipt.");
        if (branch == Branch.Entry)
        {
            await projections.ProjectAsync(new NotificationAudience.TheseSessions([session]),
                new OpeningCompleted(receipt, [new QuestClearCouponResponse(true, message.SourceSlot, message.Container) { Action = message.Action }]), ct).ConfigureAwait(false);
            await projections.ProjectAsync(new NotificationAudience.TheseSessions([session]),
                new UsageQuotaViewChanged(session.CharacterId, UsageQuotaFamily.HerasDungeon), ct).ConfigureAwait(false);
        }
        else if (branch == Branch.Fatigue)
        {
            var balance = await fatigue.BalanceOfAsync(session.CharacterId, ct).ConfigureAwait(false);
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
                new OpeningCompleted(receipt, [new QuestClearCouponResponse(true, message.SourceSlot, message.Container) { Action = message.Action }],
                    FollowingMessages: [new FatigueNotification(balance)]), ct).ConfigureAwait(false);
            await SendItemUsageAsync(context, session, ct).ConfigureAwait(false);
            logger.LogInformation("Fatigue potion used character={CharacterId} slot={Slot} remaining={Remaining}", session.CharacterId, message.SourceSlot, balance.Remaining);
        }
        else await ProjectCouponAsync(session, message, action.Coupon!, ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask SendItemUsageAsync(CommandExecutionContext context, SelectedSessionIdentity session, CancellationToken ct)
    {
        var snapshot = await units.ReadOnlyAsync(context, new("ItemUsageSnapshot", [], Footprint: CommandFootprintMode.None),
            token => ItemUsageSnapshotReader.ReadAsync(usage, sessions, clock, session, token), ct).ConfigureAwait(false);
        if (!snapshot.IsCurrent || !sessions.IsInWorld(session)) return;
        var messages = ItemUsageCountMessage.FullSnapshot(snapshot.Items).Cast<IGameNotiMessage>()
            .Concat(ItemUsageCooldownProjection.Snapshot(snapshot.Items, snapshot.At)).ToArray();
        foreach (var message in messages)
        {
            if (!sessions.IsInWorld(session)) return;
            await outbox.RegisterNotiAsync(session.ConnectionId, message, ct).ConfigureAwait(false);
        }
    }
    private async ValueTask ProjectCouponAsync(SelectedSessionIdentity session, QuestClearCouponRequest message, QuestClearCouponResult result, CancellationToken ct)
    {
        logger.LogInformation("Quest clear coupon used character={CharacterId} sourceSlot={Slot} itemId={ItemId} completedCount={Count}",
            session.CharacterId, message.SourceSlot, 10_006_007, result.CompletedQuestIds.Count);
        logger.LogDebug("Quest clear coupon completed quests character={CharacterId} questIds={QuestIds}",
            session.CharacterId, string.Join(',', result.CompletedQuestIds));

        // The absolute inventory corrects any local decrement made by the ACK. The
        // projection follows the ACK; the two quest-log projections do not depend on offers.
        var following = new List<IServerNetworkMessage>();
        try
        {
            var log = questLogs.LogOf(session.AccountId, session.CharacterId);
            var cleared = await log.ClearedAsync(ct).ConfigureAwait(false);
            var carried = await log.CarriedAsync(ct).ConfigureAwait(false);
            if (mirror is not null) carried = (await mirror.MergeAsync(session.CharacterId, new(carried), ct).ConfigureAwait(false)).Quests;
            following.AddRange(QuestClearCouponProjection.FromLog(cleared, carried));
        }
        catch (Exception failure) when ((failure is not OperationCanceledException) && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure))
        {
            logger.LogError(failure, "Quest clear coupon committed but quest log refresh failed character={CharacterId}",
                session.CharacterId);
        }
        try
        {
            var current = await currentViews.TryLoadAsync(session, ct).ConfigureAwait(false);
            var progression = await characters.Get(session.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
            if (current is not null && progression is not null)
            {
                current = await offers.RefreshAsync(current, session.AccountId, session.CharacterId,
                    progression.Level, ct).ConfigureAwait(false);
                following.Add(new AvailableQuestListMessage(progression.Level, current.World.Content.AvailableQuests));
            }
            else logger.LogWarning("Quest clear coupon committed but offer projection unavailable character={CharacterId}",
                session.CharacterId);
        }
        catch (Exception failure) when ((failure is not OperationCanceledException) && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure))
        {
            logger.LogError(failure, "Quest clear coupon committed but offer refresh failed character={CharacterId}",
                session.CharacterId);
        }
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new OpeningCompleted(result.Inventory,
                [new QuestClearCouponResponse(true, message.SourceSlot, message.Container)],
                FollowingMessages: following), ct).ConfigureAwait(false);
    }

    private ValueTask RefuseAsync(NetworkConnectionId connection, QuestClearCouponRequest request, bool sanitize, CancellationToken ct) =>
        outbox.RegisterCmdAsync(connection, new QuestClearCouponResponse(false,
            sanitize && request.SourceSlot < 0 ? (short)0 : request.SourceSlot, sanitize && request.Container != 0 ? (byte)0 : request.Container) { Action = request.Action }, ct);
    private static bool CanRefuse(Exception failure) => failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure);
    private enum Branch { Entry, Fatigue, Coupon }
    private sealed record ActionOutcome(OpeningInventoryReceipt? Inventory, QuestClearCouponResult? Coupon, Exception? Failure);
}
