using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CharacterName;
using Dfo.Server.Protocol.Game.HiddenCharacterInfo;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol.Game.TownProjection;

/// <summary>What the TownProjection slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class TownProjectionRegistration
{
    internal static IServiceCollection AddTownProjectionSlice(this IServiceCollection services)
    {
        services.TryAddSingleton<TownAreaProjectionDelivery>();
        services.AddNotificationHandler<TownParticipantPublished, TownParticipantPublishedHandler>();
        services.AddNotificationHandler<TownPositionChanged, TownPositionChangedHandler>();
        services.AddNotificationHandler<TownAreaRosterReconciled, TownAreaRosterReconciledHandler>();
        services.AddGameCmdMessageDecoder<SetUserPositionDecoder>(ClientMessageType.GameSetUserPosition);
        services.AddGameCmdMessageDecoder<SetUserAreaDecoder>(ClientMessageType.GameSetUserArea);
        services.AddGameNotiMessageEncoder<AreaUsersNotification, AreaUsersEncoder>();
        services.AddGameNotiMessageEncoder<UserPositionNotification, UserPositionEncoder>();
        services.AddGameNotiMessageEncoder<ForcedTownPositionNotification, ForcedTownPositionEncoder>();
        services.AddProtocolCommandHandler<GameSetUserAreaCommand, bool, GameTownMovementCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetUserArea, typeof(SetUserAreaRequest)));
        services.AddProtocolCommandHandler<GameSetUserPositionCommand, bool, GameTownMovementCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetUserPosition, typeof(SetUserPositionRequest)));

        // Position and area: the only inputs that move a published character, and so the only
        // way the town its next entry reports can change.
        services.RegisterProtocolMessageHandler<SetUserPositionHandler, SetUserPositionRequest, GameSetUserPositionCommand>(ClientMessageType.GameSetUserPosition);
        services.RegisterProtocolMessageHandler<SetUserAreaHandler, SetUserAreaRequest, GameSetUserAreaCommand>(ClientMessageType.GameSetUserArea);
        return services;
    }
}
