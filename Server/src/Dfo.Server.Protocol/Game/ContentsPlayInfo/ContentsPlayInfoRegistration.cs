using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ContentsPlayInfo;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ContentsPlayInfo;

/// <summary>What the ContentsPlayInfo slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ContentsPlayInfoRegistration
{
    internal static IServiceCollection AddContentsPlayInfoSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<ContentsPlayInfoMessageDecoder>(ClientMessageType.GameContentsPlayInfo);
        services.AddGameCmdMessageEncoder<ContentsPlayInfoResponse, ContentsPlayInfoResponseEncoder>();
        services.AddProtocolCommandHandler<GameContentsPlayInfoCommand, bool, GameContentsPlayInfoCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameContentsPlayInfo, typeof(ContentsPlayInfoRequest)));
        services.RegisterProtocolMessageHandler<ContentsPlayInfoHandler, ContentsPlayInfoRequest, GameContentsPlayInfoCommand>(ClientMessageType.GameContentsPlayInfo);
        return services;
    }
}
