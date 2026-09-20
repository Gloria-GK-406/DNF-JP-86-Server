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
public sealed class AcceptQuestHandler(
    ICharacterDomainService characters,
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICommandBus commandBus,
    IGameMessageSender sender,
    IQuestOfferRefreshDomainService offers,
    ILogger<AcceptQuestHandler> logger) : ClientMessageHandler<AcceptQuestRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        AcceptQuestRequest message,
        CancellationToken cancellationToken)
    {
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
            return;
        }

        var outcome = await commandBus.SendAsync(
            new AcceptQuestCommand(session.AccountId, session.CharacterId, message.QuestId),
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
            return;

        if (outcome.AccountMaterials is { Count: > 0 } accountMaterials)
        {
            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [GameInventoryDelivery.VirtualResourceNotification(
                        [.. accountMaterials.Select(material => new InventoryRuntimeValueState(
                            material.VirtualSlotIndex,
                            material.ItemId,
                            material.FinalCount))],
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
            await sender.SendExplicitAsync(
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
            return;
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
            return;
        result = await offers.RefreshAsync(
            result,
            session.AccountId,
            session.CharacterId,
            currentProgress.Level,
            cancellationToken).ConfigureAwait(false);

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [
                    new AvailableQuestListMessage(
                        currentProgress.Level, result.World.Content.AvailableQuests),
                ],
                []),
            cancellationToken).ConfigureAwait(false);
    }

    private ValueTask AnswerAsync(
        NetworkDispatchContext context,
        AcceptQuestAcknowledgement acknowledgement,
        CancellationToken cancellationToken) =>
        sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([acknowledgement], []),
            cancellationToken);
}
