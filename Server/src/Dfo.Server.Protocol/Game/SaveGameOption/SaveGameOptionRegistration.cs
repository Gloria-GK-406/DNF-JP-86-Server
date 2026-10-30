using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.SaveGameOption;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.SaveGameOption;

/// <summary>What the SaveGameOption slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class SaveGameOptionRegistration
{
    internal static IServiceCollection AddSaveGameOptionSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<SaveGameOption1MessageDecoder>(ClientMessageType.GameSaveGameOption1);
        services.RegisterProtocolMessageHandler<SaveGameOption1Handler, SaveGameOption1Request, GameSaveGameOption1Command>(ClientMessageType.GameSaveGameOption1);
        services.AddProtocolCommandHandler<GameSaveGameOption1Command, bool, GameSaveGameOption1CommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSaveGameOption1, typeof(SaveGameOption1Request)));
        return services;
    }
}
