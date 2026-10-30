using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CharacterName;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.CharacterName;

/// <summary>What the CharacterName slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class CharacterNameRegistration
{
    internal static IServiceCollection AddCharacterNameSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<CheckCharacterNameMessageDecoder>(ClientMessageType.GameCheckCharacterName);
        services.AddGameCmdMessageEncoder<CheckCharacterNameResponse, CheckCharacterNameResponseEncoder>();
        services.AddProtocolCommandHandler<GameCheckCharacterNameCommand, CharacterNameStatus, GameCheckCharacterNameCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCheckCharacterName, typeof(CheckCharacterNameRequest)));
        services.RegisterProtocolMessageHandler<CheckCharacterNameHandler, CheckCharacterNameRequest, GameCheckCharacterNameCommand>(ClientMessageType.GameCheckCharacterName);
        return services;
    }
}
