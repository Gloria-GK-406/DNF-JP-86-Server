using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.AccountRoster;

public sealed record QueryOtherCharacterDetailCommand(
    SelectedSessionIdentity Requester,
    ushort TargetWireUserId) : ICommand<CharacterDetailQueryResult>;

public sealed class QueryOtherCharacterDetailCommandHandler(
    IOtherCharacterDetailQueryDomainService details)
    : ICommandHandler<QueryOtherCharacterDetailCommand, CharacterDetailQueryResult>
{
    public ValueTask<CharacterDetailQueryResult> HandleAsync(
        CommandExecutionContext context,
        QueryOtherCharacterDetailCommand command,
        CancellationToken cancellationToken) =>
        details.QueryAsync(command.Requester, command.TargetWireUserId, cancellationToken);
}

public sealed record OtherCharacterDetailRequested(
    OtherCharacterDetailProjection Projection)
    : INotification<OtherCharacterDetailRequested>;

public sealed record OtherCharacterDetailResponse(
    SelectCharacterResult TargetState,
    DateTimeOffset ProjectedAt) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameOtherCharacterDetail;
}

public sealed class OtherCharacterDetailRequestedHandler(
    ISelectedSessionDomainService sessions)
    : INotificationHandler<OtherCharacterDetailRequested>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        OtherCharacterDetailRequested notification,
        CancellationToken cancellationToken)
    {
        var projection = notification.Projection;
        var requester = projection.Requester;
        var target = projection.Target;
        var isExactRequester = recipient.CharacterId == requester.CharacterId
            && recipient.ConnectionId == requester.ConnectionId
            && sessions.IsInWorld(requester);
        var isExactTarget = target.ListenerPort == requester.ListenerPort
            && sessions.IsInWorld(target)
            && projection.TargetState.Character.Identity.CharacterId == target.CharacterId;

        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            isExactRequester && isExactTarget
                ? [new OtherCharacterDetailResponse(
                    projection.TargetState, projection.ProjectedAt)]
                : []);
    }
}

internal sealed class OtherCharacterDetailResponseEncoder
    : GameNotiMessageEncoder<OtherCharacterDetailResponse>
{
    private const uint QueryAnswer = 1;

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        OtherCharacterDetailResponse message)
    {
        var state = message.TargetState;
        var equipment = state.CharacterInventory.Containers
            .SingleOrDefault(value => value.ContainerType == InventoryContainerType.Equipment)
            ?.Items ?? [];
        var body = StateSynchronizationPayloadCodec.EncodeOtherCharacterDetail(
            state.Character,
            state.CharacterEntry.Public,
            state.CharacterEntry.Skills,
            equipment,
            message.ProjectedAt);
        return
        [
            new GameMessagePayload(
                GameProtocol.AccountCharacterRosterNotificationType,
                body,
                Checksum: QueryAnswer),
        ];
    }
}

public sealed class GetOtherCharacterDetailHandler(
    ISelectedSessionDomainService sessions,
    ICommandBus commands,
    IEventService notifications,
    ILogger<GetOtherCharacterDetailHandler> logger)
    : ClientMessageHandler<GetOtherCharacterDetailRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        GetOtherCharacterDetailRequest message,
        CancellationToken cancellationToken)
    {
        var requester = sessions.FindByConnection(context.Connection.ConnectionId);
        if (requester is null)
        {
            logger.LogInformation(
                "GET_USERINFO mode=3 ignored for non-current connection {ConnectionId}",
                context.Connection.ConnectionId);
            return;
        }

        if (!sessions.TryGetInWorld(message.TargetWireUserId, out var target))
        {
            logger.LogInformation(
                "GET_USERINFO mode=3 requester={Requester} target={Target} refused={Failure}",
                requester.WireUserId,
                message.TargetWireUserId,
                CharacterDetailQueryFailure.TargetNotCurrent);
            return;
        }

        CharacterDetailQueryResult result;
        if (!sessions.IsInWorld(requester))
        {
            result = CharacterDetailQueryResult.Reject(CharacterDetailQueryFailure.RequesterNotCurrent);
        }
        else if (!sessions.IsInWorld(target))
        {
            result = CharacterDetailQueryResult.Reject(CharacterDetailQueryFailure.TargetNotCurrent);
        }
        else
        {
            result = await commands.SendAsync(
                new QueryOtherCharacterDetailCommand(requester, message.TargetWireUserId),
                cancellationToken).ConfigureAwait(false);
        }

        if (result.Projection is { } projection)
        {
            await notifications.ProjectAsync(
                new NotificationAudience.TheseCharacters([requester.CharacterId]),
                new OtherCharacterDetailRequested(projection),
                cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "GET_USERINFO mode=3 requester={Requester} target={Target} accepted={Accepted} failure={Failure}",
            requester.WireUserId,
            message.TargetWireUserId,
            result.Succeeded,
            result.Failure);
    }
}
