using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DeleteCharacter;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.DeleteCharacter;

/// <summary>What the DeleteCharacter slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class DeleteCharacterRegistration
{
    internal static IServiceCollection AddDeleteCharacterSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<DeleteCharacterMessageDecoder>(
            GameProtocol.DeleteCharacterType, ClientMessageType.GameDeleteCharacter);
        services.AddGameCmdMessageEncoder<DeleteCharacterResponse, DeleteCharacterResponseEncoder>();
        services.AddProtocolCommandHandler<GameDeleteCharacterCommand, CharacterDeletionResult, GameDeleteCharacterCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameDeleteCharacter, typeof(DeleteCharacterRequest)));
        services.RegisterProtocolMessageHandler<DeleteCharacterHandler, DeleteCharacterRequest, GameDeleteCharacterCommand>(ClientMessageType.GameDeleteCharacter);
        return services;
    }
}
