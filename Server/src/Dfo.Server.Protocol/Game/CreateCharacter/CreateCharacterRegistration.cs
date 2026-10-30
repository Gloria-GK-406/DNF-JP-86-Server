using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CreateCharacter;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.CreateCharacter;

/// <summary>What the CreateCharacter slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class CreateCharacterRegistration
{
    internal static IServiceCollection AddCreateCharacterSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<CreateCharacterMessageDecoder>(
            GameProtocol.CreateCharacterType, ClientMessageType.GameCreateCharacter);
        services.AddGameCmdMessageEncoder<CreateCharacterResponse, CreateCharacterResponseEncoder>();
        services.AddProtocolCommandHandler<GameCreateCharacterCommand, CharacterCreationResult, GameCreateCharacterCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCreateCharacter, typeof(CreateCharacterRequest)));
        services.RegisterProtocolMessageHandler<CreateCharacterHandler, CreateCharacterRequest, GameCreateCharacterCommand>(ClientMessageType.GameCreateCharacter);
        return services;
    }
}
