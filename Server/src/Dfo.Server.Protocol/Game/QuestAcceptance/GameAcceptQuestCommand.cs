using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.QuestAcceptance;

/// <summary>
/// Takes the quest on, answers with what it opened at, and refreshes the offer list.
/// </summary>
/// <remarks>
/// After acknowledgement, an accepted quest refreshes the offer list from current character
/// progression, career and quest-log facts. The refreshed list follows in the existing frame order.
///
/// <b>Answering at all is what matters most here.</b> Before this existed the client sent the
/// request, waited, and sent it again: the two acceptances observed live arrived twice each.
/// </remarks>
public sealed record GameAcceptQuestCommand(NetworkDispatchContext Dispatch, AcceptQuestRequest Input) : IProtocolCommand<QuestAcceptOutcome>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameAcceptQuestCommandHandler(
    ICharacterDomainService characters,
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    IQuestOfferRefreshDomainService offers,
    ILogger<GameAcceptQuestCommandHandler> logger,
    IQuestDefinitionDomainService quests,
    ICharacterQuestLogDomainService logs,
    ICharacterContainerDomainService containers,
    IQuestProgressDomainService questProgress,
    IItemDeliveryDomainService delivery,
    ICycleDungeonEntryDomainService? cycles = null,
    IMirrorAradQuestDomainService? mirror = null,
    Dfo.Server.Abstractions.Marriage.IMarriageQuestDomainService? marriage = null,
    ISouthernDaleQuestDomainService? southern = null)
    : ICommandHandler<GameAcceptQuestCommand, QuestAcceptOutcome>
{
    public async ValueTask<QuestAcceptOutcome> HandleAsync(CommandExecutionContext execution, GameAcceptQuestCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            // No character is selected, so there is nothing to carry the quest. Refused rather
            // than ignored: silence is what made the client repeat itself.
            logger.LogWarning(
                "Accept quest {ConnectionId}: quest {Quest} with no character selected",
                context.Connection.ConnectionId, message.QuestId);
            await AnswerAsync(
                context,
                new AcceptQuestAcknowledgement(
                    false, message.QuestId, 0, [], (byte)QuestAcceptRefusal.Unknown),
                cancellationToken).ConfigureAwait(false);
            return QuestAcceptOutcome.Refuse(message.QuestId, QuestAcceptRefusal.Unknown);
        }

        var outcome = await units.WriteAsync(execution, new("AcceptQuest", []),
            (_, token) => AcceptAsync(new(session.AccountId, session.CharacterId, message.QuestId), token),
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Accept quest {ConnectionId} account={Account}: quest={Quest} {Verdict} trigger={Trigger} items={Items}",
            context.Connection.ConnectionId,
            session.AccountId,
            message.QuestId,
            outcome.Accepted ? "accepted" : $"refused({outcome.Refusal})",
            outcome.InitialTrigger,
            outcome.Items.Count);

        await AnswerAsync(
            context,
            new AcceptQuestAcknowledgement(
                outcome.Accepted,
                outcome.QuestId,
                outcome.InitialTrigger,
                outcome.Items,
                (byte)(outcome.Refusal ?? QuestAcceptRefusal.Unknown)),
            cancellationToken).ConfigureAwait(false);

        if (!outcome.Accepted)
            return outcome;

        if (outcome.AccountMaterials is { Count: > 0 } accountMaterials)
        {
            await outbox.RegisterExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [GameInventoryDelivery.VirtualResourceNotification(
                        [.. accountMaterials.Select(material => new InventoryRuntimeValueState(
                            material.SlotIndex,
                            material.ItemId,
                            material.Value))],
                        DateTimeOffset.UtcNow)],
                    []),
                cancellationToken).ConfigureAwait(false);
        }

        if (outcome.QuestProgress is { } progress)
        {
            var projected = progress.Quests.OrderBy(quest => quest.Slot)
                .Select(quest => new AcceptedQuestProgressState(
                    checked((ushort)quest.QuestId),
                    unchecked((uint)quest.TriggerValue)))
                .ToArray();
            await outbox.RegisterExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [new AcceptedQuestProgressMessage(projected)], []),
                cancellationToken).ConfigureAwait(false);
        }

        var result = await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            logger.LogWarning(
                "Accept quest {ConnectionId}: quest {Quest} was taken but the session holds no "
                    + "entry result, so the offer list was not refreshed",
                context.Connection.ConnectionId, message.QuestId);
            return outcome;
        }

        // The offer list is worked out again, because taking a quest on changes one of its inputs.
        // The quest just accepted leaves it, which is a deliberate difference from the reference
        // server: its list builder is never given the carried set and therefore names a quest the
        // character is already holding. The client believes that list -- it raised its own accept
        // prompt again for a quest just accepted, and the second acceptance was refused. Seen live
        // on 2026-08-27 and reproduced by the reference server on the same client. Recorded in
        // Docs/ProtocolScaffoldDissonances.md.
        var currentProgress = await characters.Get(session.CharacterId).ProgressAsync(cancellationToken).ConfigureAwait(false);
        if (currentProgress is null)
            return outcome;
        result = await offers.RefreshAsync(
            result,
            session.AccountId,
            session.CharacterId,
            currentProgress.Level,
            cancellationToken).ConfigureAwait(false);

        await outbox.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [
                    new AvailableQuestListMessage(
                        currentProgress.Level, result.World.Content.AvailableQuests),
                ],
                []),
            cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    private ValueTask AnswerAsync(
        NetworkDispatchContext context,
        AcceptQuestAcknowledgement acknowledgement,
        CancellationToken cancellationToken) =>
        outbox.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([acknowledgement], []),
            cancellationToken);

    internal async ValueTask<QuestAcceptOutcome> AcceptAsync(
        QuestAcceptanceInput command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var definition = await quests.FindAsync(command.QuestId, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return QuestAcceptOutcome.Refuse(command.QuestId, QuestAcceptRefusal.Unknown);

        if (definition.Grade == QuestGrade.Crack)
        {
            if (mirror is null) return QuestAcceptOutcome.Refuse(command.QuestId, QuestAcceptRefusal.Unknown);
            var accepted = await mirror.AcceptAsync(command.AccountId, command.CharacterId, command.QuestId, cancellationToken).ConfigureAwait(false);
            return accepted.Accepted
                ? new(true, command.QuestId, unchecked((uint)accepted.TriggerValue), [])
                : QuestAcceptOutcome.Refuse(command.QuestId, WireCodeFor(accepted.Refusal));
        }
        var log = logs.LogOf(command.AccountId, command.CharacterId);
        if (definition.Grade == QuestGrade.Wedding && (marriage is null
            || !await marriage.CanAcceptAsync(command.AccountId, command.CharacterId, command.QuestId, cancellationToken).ConfigureAwait(false)))
            return QuestAcceptOutcome.Refuse(command.QuestId, QuestAcceptRefusal.Unknown);
        if (definition.Grade == QuestGrade.Circle
            && (cycles is null || !await cycles.CanAcceptAsync(command.AccountId, command.CharacterId,
                command.QuestId, cancellationToken).ConfigureAwait(false)))
            return QuestAcceptOutcome.Refuse(command.QuestId, QuestAcceptRefusal.Unknown);
        var carried = await log.CarriedAsync(cancellationToken).ConfigureAwait(false);
        // A21's successful ACK inserts a new client quest; a duplicate returns null and crashes.
        // CMD001F refusal 00 12 bypasses that insertion (protocol/game/client-to-server/0x001F.md).
        if (carried.Any(x => x.QuestId == command.QuestId))
            return QuestAcceptOutcome.Refuse(command.QuestId, QuestAcceptRefusal.AlreadyActive);
        if (command.QuestId == ISouthernDaleQuestDomainService.QuestId && (southern is null
            || !await southern.CanAcceptAsync(command.CharacterId, cancellationToken).ConfigureAwait(false)))
            return QuestAcceptOutcome.Refuse(command.QuestId, QuestAcceptRefusal.Unknown);
        var taken = await log.AcceptAsync(definition, cancellationToken).ConfigureAwait(false);
        if (!taken.Accepted)
            return QuestAcceptOutcome.Refuse(command.QuestId, WireCodeFor(taken.Refusal));
        if (definition.Grade == QuestGrade.Wedding)
            await marriage!.BindAsync(command.AccountId, command.CharacterId, command.QuestId, cancellationToken).ConfigureAwait(false);

        var declared = definition.EventItems ?? [];
        var placed = new List<AcceptedQuestItem>(declared.Count);
        var accountChanges = new List<InventoryRuntimeValueState>();
        if (declared.Count > 0)
        {
            var container = containers.FindContainer(
                command.CharacterId, InventoryContainerType.Main);
            if (container is null)
            {
                return await this.HandBackAsync(log, command.QuestId, cancellationToken)
                    .ConfigureAwait(false);
            }

            var receipt = await delivery.DeliverAsync(command.AccountId, command.CharacterId,
                declared.Select(x => new ItemDeliveryGrant(x.ItemId, x.Count)).ToArray(), "任务授予物品", cancellationToken, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
            foreach (var item in receipt.Inventory)
            {
                placed.Add(new(checked((ushort)ItemGrantWireProjection.Index(item)), (uint)item.Snapshot.ItemId, (uint)item.Count));
                if (ItemGrantWireProjection.Shared(item) is { } shared) accountChanges.Add(shared);
            }
        }

        var progress = placed.Count > 0 || definition.SeekingItems is { Count: > 0 }
            ? (await questProgress.ReportAsync(
                command.AccountId, command.CharacterId,
                [new QuestHoldingsChanged()], cancellationToken).ConfigureAwait(false))
                .NotificationSnapshot
            : null;
        if (progress is null && taken.TriggerValue != definition.InitialTrigger)
        {
            progress = new QuestProgressSnapshot(await log.CarriedAsync(cancellationToken)
                .ConfigureAwait(false));
        }
        if (progress is not null && mirror is not null)
            progress = await mirror.MergeAsync(command.CharacterId, progress, cancellationToken).ConfigureAwait(false);
        return new QuestAcceptOutcome(
            true, command.QuestId, definition.InitialTrigger, placed,
            QuestProgress: progress,
            AccountMaterials: accountChanges);
    }

    // Missing the main container refuses the acceptance before any item delivery.
    // Grants themselves are handled by the transactional inventory/mail delivery service.
    private async ValueTask<QuestAcceptOutcome> HandBackAsync(
        IQuestLog log, ushort questId, CancellationToken cancellationToken)
    {
        await log.GiveUpAsync(questId, cancellationToken).ConfigureAwait(false);
        return QuestAcceptOutcome.Refuse(questId, QuestAcceptRefusal.NoRoomForItems);
    }

    private static QuestAcceptRefusal WireCodeFor(QuestRefusal? refusal) => refusal switch
    {
        QuestRefusal.AlreadyCarried => QuestAcceptRefusal.AlreadyActive,
        QuestRefusal.AlreadyCleared => QuestAcceptRefusal.AlreadyActive,
        QuestRefusal.NoFreeSlot => QuestAcceptRefusal.ActiveListFull,
        QuestRefusal.ManagedByAchievement => QuestAcceptRefusal.Unknown,
        QuestRefusal.UnsupportedObjective => QuestAcceptRefusal.Unknown,
        _ => throw new ArgumentOutOfRangeException(
            nameof(refusal), refusal, "No code was decided for this refusal."),
    };
}
