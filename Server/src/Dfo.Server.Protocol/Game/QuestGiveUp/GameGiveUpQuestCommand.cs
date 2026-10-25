using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.QuestGiveUp;

public sealed record GameGiveUpQuestCommand(NetworkDispatchContext Dispatch, GiveUpQuestRequest Input) : IProtocolCommand<GiveUpQuestOutcome>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameGiveUpQuestCommandHandler(
    ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterDomainService characters,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    IQuestOfferRefreshDomainService offers,
    ILogger<GameGiveUpQuestCommandHandler> logger,
    ICharacterQuestLogDomainService logs,
    IQuestDefinitionDomainService? definitions = null,
    IMirrorAradQuestDomainService? mirror = null)
    : ICommandHandler<GameGiveUpQuestCommand, GiveUpQuestOutcome>
{
    public async ValueTask<GiveUpQuestOutcome> HandleAsync(CommandExecutionContext execution, GameGiveUpQuestCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        var outcome = session is null
            ? GiveUpQuestOutcome.Refused(message.QuestId)
            : await units.WriteAsync(execution, new("GiveUpQuest", []),
                (_, token) => GiveUpAsync(new(session.AccountId, session.CharacterId, message.QuestId), token),
                cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Give up quest {ConnectionId} quest={QuestId} givenUp={GivenUp}",
            context.Connection.ConnectionId, message.QuestId, outcome.GivenUp);
        await outbox.RegisterExplicitAsync(context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([
                new GiveUpQuestAcknowledgement(outcome.GivenUp, message.QuestId,
                    outcome.GivenUp ? (byte)0 : (byte)19)
            ], []), cancellationToken).ConfigureAwait(false);

        if (!outcome.GivenUp || session is null || outcome.QuestProgress is not { } progress)
            return outcome;

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

        await outbox.RegisterExplicitAsync(context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(messages, []), cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    internal async ValueTask<GiveUpQuestOutcome> GiveUpAsync(
        QuestGiveUpInput command, CancellationToken cancellationToken)
    {
        var definition = definitions is null ? null : await definitions.FindAsync(command.QuestId, cancellationToken).ConfigureAwait(false);
        if (definition?.Grade == Dfo.Server.Abstractions.Content.QuestGrade.Crack)
        {
            if (mirror is null || !await mirror.GiveUpAsync(command.AccountId, command.CharacterId, command.QuestId, cancellationToken).ConfigureAwait(false))
                return GiveUpQuestOutcome.Refused(command.QuestId);
            return new(true, command.QuestId, await mirror.SnapshotAsync(command.AccountId, command.CharacterId, cancellationToken).ConfigureAwait(false));
        }
        var log = logs.LogOf(command.AccountId, command.CharacterId);
        if (!await log.GiveUpAsync(command.QuestId, cancellationToken).ConfigureAwait(false))
            return GiveUpQuestOutcome.Refused(command.QuestId);

        var snapshot = new QuestProgressSnapshot(await log.CarriedAsync(cancellationToken).ConfigureAwait(false));
        if (mirror is not null) snapshot = await mirror.MergeAsync(command.CharacterId, snapshot, cancellationToken).ConfigureAwait(false);
        return new GiveUpQuestOutcome(true, command.QuestId, snapshot);
    }
}
