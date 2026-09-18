using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.QuestGiveUp;

public sealed class GiveUpQuestHandler(
    ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterDomainService characters,
    ICommandBus commands,
    IGameMessageSender sender,
    IQuestOfferRefreshDomainService offers,
    ILogger<GiveUpQuestHandler> logger) : ClientMessageHandler<GiveUpQuestRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, GiveUpQuestRequest message, CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        var outcome = session is null
            ? GiveUpQuestOutcome.Refused(message.QuestId)
            : await commands.SendAsync(new GiveUpQuestCommand(
                session.AccountId, session.CharacterId, message.QuestId), cancellationToken)
                .ConfigureAwait(false);

        logger.LogInformation("Give up quest {ConnectionId} quest={QuestId} givenUp={GivenUp}",
            context.Connection.ConnectionId, message.QuestId, outcome.GivenUp);
        await sender.SendExplicitAsync(context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([
                new GiveUpQuestAcknowledgement(outcome.GivenUp, message.QuestId,
                    outcome.GivenUp ? (byte)0 : (byte)19)
            ], []), cancellationToken).ConfigureAwait(false);

        if (!outcome.GivenUp || session is null || outcome.QuestProgress is not { } progress)
            return;

        var messages = new List<IGameServerMessage>
        {
            new AcceptedQuestProgressMessage(
                [.. progress.Quests.OrderBy(quest => quest.Slot)
                    .Select(quest => new AcceptedQuestProgressState(
                        checked((ushort)quest.QuestId), unchecked((uint)quest.TriggerValue)))])
        };

        var entry = await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        var current = await characters.Get(session.CharacterId).ProgressAsync(cancellationToken)
            .ConfigureAwait(false);
        if (entry is not null && current is not null)
        {
            var refreshed = await offers.RefreshAsync(entry, session.AccountId, session.CharacterId,
                current.Level, cancellationToken).ConfigureAwait(false);
            messages.Add(new AvailableQuestListMessage(current.Level, refreshed.World.Content.AvailableQuests));
        }

        await sender.SendExplicitAsync(context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(messages, []), cancellationToken).ConfigureAwait(false);
    }
}
