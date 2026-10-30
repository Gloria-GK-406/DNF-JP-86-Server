using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MercenaryInfo;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.MercenaryInfo;

/// <summary>What the MercenaryInfo slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class MercenaryInfoRegistration
{
    internal static IServiceCollection AddMercenaryInfoSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<MercenaryInfoMessageDecoder>(ClientMessageType.GameMercenaryInfo);
        services.AddGameCmdMessageEncoder<MercenaryInfoResponse, MercenaryInfoResponseEncoder>();
        services.AddProtocolCommandHandler<GameMercenaryInfoCommand, bool, GameMercenaryInfoCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameMercenaryInfo, typeof(MercenaryInfoRequest)));
        services.RegisterProtocolMessageHandler<MercenaryInfoHandler, MercenaryInfoRequest, GameMercenaryInfoCommand>(ClientMessageType.GameMercenaryInfo);
        return services;
    }
}
