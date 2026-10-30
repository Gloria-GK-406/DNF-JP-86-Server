using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.SaveHotkeys;

/// <summary>What the SaveHotkeys slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class SaveHotkeysRegistration
{
    internal static IServiceCollection AddSaveHotkeysSlice(this IServiceCollection services)
    {
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.SaveHotkeys.SaveHotkeysHandler, Dfo.Server.Protocol.Game.SaveHotkeys.SaveHotkeysRequest, Dfo.Server.Protocol.Game.SaveHotkeys.GameSaveHotkeysCommand>(ClientMessageType.GameSaveHotkeys);
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.SaveHotkeys.GameSaveHotkeysCommand, bool, Dfo.Server.Protocol.Game.SaveHotkeys.GameSaveHotkeysCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSaveHotkeys, typeof(Dfo.Server.Protocol.Game.SaveHotkeys.SaveHotkeysRequest)));
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.SaveHotkeys.SaveHotkeysDecoder>(0x00C6, ClientMessageType.GameSaveHotkeys);
        return services;
    }
}
