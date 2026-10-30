using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MapEntryReports;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.MapEntryReports;

/// <summary>What the MapEntryReports slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class MapEntryReportsRegistration
{
    internal static IServiceCollection AddMapEntryReportsSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<CeraQueryDecoder>(
            GameProtocol.CeraQueryType, ClientMessageType.GameCeraQuery);
        services.AddGameCmdMessageDecoder<SecurityStatusDecoder>(
            GameProtocol.SecurityStatusType, ClientMessageType.GameSecurityStatus);
        services.AddGameCmdMessageDecoder<RuntimeTingStatisticDecoder>(
            GameProtocol.RuntimeTingStatisticType, ClientMessageType.GameRuntimeTingStatistic);
        services.AddGameCmdMessageEncoder<SecurityStatusAcknowledgement, SecurityStatusAcknowledgementEncoder>();

        // One handler, the remaining wire keys in the map-entry report family.
        services.AddProtocolCommandHandler<GameMapEntryReportCommand, bool, GameMapEntryReportCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCeraQuery, typeof(CeraQueryReport)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameRuntimeTingStatistic, typeof(RuntimeTingStatisticReport)));
        services.RegisterProtocolMessageHandler<MapEntryReportHandler, IMapEntryReport, GameMapEntryReportCommand>(ClientMessageType.GameCeraQuery, typeof(CeraQueryReport));
        services.AddProtocolCommandHandler<GameSecurityStatusCommand, bool, GameSecurityStatusCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSecurityStatus, typeof(SecurityStatusReport)));
        services.RegisterProtocolMessageHandler<SecurityStatusHandler, SecurityStatusReport, GameSecurityStatusCommand>(ClientMessageType.GameSecurityStatus);
        services.RegisterProtocolMessageHandler<MapEntryReportHandler, IMapEntryReport, GameMapEntryReportCommand>(ClientMessageType.GameRuntimeTingStatistic, typeof(RuntimeTingStatisticReport));
        return services;
    }
}
