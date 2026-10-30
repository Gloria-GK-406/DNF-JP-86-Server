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
    IQuestAcceptanceDomainService acceptance)
    : ICommandHandler<GameAcceptQuestCommand, QuestAcceptOutcome>
{
    public async ValueTask<QuestAcceptOutcome> HandleAsync(CommandExecutionContext execution, GameAcceptQuestCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        if (!selectedSessions.TryGetInWorld(context.Connection.ConnectionId, out var session))
        {
            // No character in the world on this connection, so there is nothing to carry the quest.
            // Refused rather than ignored: silence is what made the client repeat itself.
            logger.LogWarning(
                "Accept quest {ConnectionId}: quest {Quest} with no character in the world",
                context.Connection.ConnectionId, message.QuestId);
            await AnswerAsync(
                context,
                new AcceptQuestAcknowledgement(
                    false, message.QuestId, 0, [], (byte)QuestAcceptRefusal.Unknown),
                cancellationToken).ConfigureAwait(false);
            return QuestAcceptOutcome.Refuse(message.QuestId, QuestAcceptRefusal.Unknown);
        }

        var outcome = await units.WriteAsync(execution, new("AcceptQuest", [new(session, context.Connection, message.Metadata)]),
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
        var accepted = await acceptance.AcceptAsync(
            command.AccountId, command.CharacterId, command.QuestId, cancellationToken).ConfigureAwait(false);
        if (accepted.Refusal is { } refusal)
            return QuestAcceptOutcome.Refuse(command.QuestId, WireCodeFor(refusal));

        var placed = new List<AcceptedQuestItem>(accepted.Items.Count);
        var accountChanges = new List<InventoryRuntimeValueState>();
        foreach (var item in accepted.Items)
        {
            placed.Add(new(checked((ushort)ItemGrantWireProjection.Index(item)), (uint)item.Snapshot.ItemId, (uint)item.Count));
            if (ItemGrantWireProjection.Shared(item) is { } shared) accountChanges.Add(shared);
        }
        return new QuestAcceptOutcome(
            true, command.QuestId, accepted.InitialTrigger, placed,
            QuestProgress: accepted.QuestProgress,
            AccountMaterials: accountChanges);
    }

    private static QuestAcceptRefusal WireCodeFor(QuestAcceptanceRefusal refusal) => refusal switch
    {
        QuestAcceptanceRefusal.NotAcceptable => QuestAcceptRefusal.Unknown,
        QuestAcceptanceRefusal.AlreadyCarried => QuestAcceptRefusal.AlreadyActive,
        QuestAcceptanceRefusal.AlreadyCleared => QuestAcceptRefusal.AlreadyActive,
        QuestAcceptanceRefusal.NoFreeSlot => QuestAcceptRefusal.ActiveListFull,
        QuestAcceptanceRefusal.ManagedByAchievement => QuestAcceptRefusal.Unknown,
        QuestAcceptanceRefusal.UnsupportedObjective => QuestAcceptRefusal.Unknown,
        QuestAcceptanceRefusal.NoRoomForItems => QuestAcceptRefusal.NoRoomForItems,
        _ => throw new ArgumentOutOfRangeException(
            nameof(refusal), refusal, "No code was decided for this refusal."),
    };
}
