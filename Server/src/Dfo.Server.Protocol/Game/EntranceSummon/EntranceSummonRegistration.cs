using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.EntranceSummon;

/// <summary>What the EntranceSummon slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class EntranceSummonRegistration
{
    internal static IServiceCollection AddEntranceSummonSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageEncoder<EntranceSummonResponse, EntranceSummonEncoder>();
        services.AddGameCmdMessageDecoder<EntranceSummonDecoder>(ClientMessageType.GameEntranceSummon);
        services.AddProtocolCommandHandler<GameEntranceSummonCommand, bool, GameEntranceSummonCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameEntranceSummon, typeof(EntranceSummonRequest)));
        services.RegisterProtocolMessageHandler<EntranceSummonHandler, EntranceSummonRequest, GameEntranceSummonCommand>(
            ClientMessageType.GameEntranceSummon);
        return services;
    }
}
