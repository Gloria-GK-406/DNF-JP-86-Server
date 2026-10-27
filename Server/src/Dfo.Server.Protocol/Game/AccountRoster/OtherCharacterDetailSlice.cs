using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.AccountRoster;

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

public sealed class GetOtherCharacterDetailHandler(ICommandBus commands)
    : ClientMessageHandler<GetOtherCharacterDetailRequest>, IProtocolCommandMessageHandler<GameOtherCharacterDetailCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        GetOtherCharacterDetailRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameOtherCharacterDetailCommand(context, message), cancellationToken)
            .ConfigureAwait(false);
}
