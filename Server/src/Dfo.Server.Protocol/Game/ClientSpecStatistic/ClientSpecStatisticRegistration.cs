using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ClientSpecStatistic;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ClientSpecStatistic;

/// <summary>What the ClientSpecStatistic slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ClientSpecStatisticRegistration
{
    internal static IServiceCollection AddClientSpecStatisticSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<ClientSpecStatisticMessageDecoder>(
            GameProtocol.ClientSpecStatisticType, ClientMessageType.GameClientSpecStatistic);
        services.AddProtocolCommandHandler<GameClientSpecStatisticCommand, bool, GameClientSpecStatisticCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameClientSpecStatistic, typeof(ClientSpecStatisticReportRequest)));
        services.RegisterProtocolMessageHandler<ClientSpecStatisticHandler, ClientSpecStatisticReportRequest, GameClientSpecStatisticCommand>(ClientMessageType.GameClientSpecStatistic);
        return services;
    }
}
