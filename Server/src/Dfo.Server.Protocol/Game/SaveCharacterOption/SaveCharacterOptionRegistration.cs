using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.SaveCharacterOption;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.SaveCharacterOption;

/// <summary>What the SaveCharacterOption slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class SaveCharacterOptionRegistration
{
    internal static IServiceCollection AddSaveCharacterOptionSlice(this IServiceCollection services)
    {
        services.AddProtocolCommandHandler<GameSaveCharacterOptionCommand, bool, GameSaveCharacterOptionCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSaveCharacterOption, typeof(SaveCharacterOptionRequest)));
        services.RegisterProtocolMessageHandler<SaveCharacterOptionHandler, SaveCharacterOptionRequest, GameSaveCharacterOptionCommand>(ClientMessageType.GameSaveCharacterOption);
        services.AddGameCmdMessageDecoder<SaveCharacterOptionDecoder>(ClientMessageType.GameSaveCharacterOption);
        return services;
    }
}
