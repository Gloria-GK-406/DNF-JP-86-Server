using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

/// <summary>What the SelectCharacter slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class SelectCharacterRegistration
{
    internal static IServiceCollection AddSelectCharacterSlice(this IServiceCollection services)
    {
        services.TryAddSingleton(SelectCharacterDeliveryOptions.Everything);
        services.TryAddSingleton<ISelectedCharacterStateMessageFactory, SelectedCharacterStateMessageFactoryService>();
        services.AddGameCmdMessageDecoder<SelectCharacterMessageDecoder>(
            GameProtocol.SelectCharacterType, ClientMessageType.GameSelectCharacter);
        services.AddProtocolCommandHandler<GameSelectCharacterCommand, bool, GameSelectCharacterCommandHandler>(
            CommandSelectedSessionCleanupCapability.SelectedSessionLifecycle,
            new ProtocolCommandMessageBinding(ClientMessageType.GameSelectCharacter, typeof(SelectCharacterRequest)));
        services.AddNotificationHandler<PvpParticipantPublished, PvpParticipantPublishedHandler>();
        services.RegisterProtocolMessageHandler<SelectCharacterHandler, SelectCharacterRequest, GameSelectCharacterCommand>(ClientMessageType.GameSelectCharacter);
        return services;
    }
}
