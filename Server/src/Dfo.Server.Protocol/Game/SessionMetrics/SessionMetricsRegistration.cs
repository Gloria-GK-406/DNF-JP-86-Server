using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.SessionMetrics;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.SessionMetrics;

/// <summary>What the SessionMetrics slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class SessionMetricsRegistration
{
    internal static IServiceCollection AddSessionMetricsSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<SessionMetricsMessageDecoder>(
            GameProtocol.SessionMetricsType, ClientMessageType.GameSessionMetrics);
        services.AddProtocolCommandHandler<GameSessionMetricsCommand, bool, GameSessionMetricsCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSessionMetrics, typeof(SessionMetricsReportRequest)));
        services.RegisterProtocolMessageHandler<SessionMetricsHandler, SessionMetricsReportRequest, GameSessionMetricsCommand>(ClientMessageType.GameSessionMetrics);
        return services;
    }
}
