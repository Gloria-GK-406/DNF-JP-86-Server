using Dfo.Server.Application.World;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CharacterName;
using Dfo.Server.Protocol.Game.ClientHardwareDetail;
using Dfo.Server.Protocol.Game.ClientSpecProfile;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.AvatarCompound;
using Dfo.Server.Protocol.Game.UnansweredReports;
using Dfo.Server.Protocol.Game.HiddenCharacterInfo;
using Dfo.Server.Protocol.Game.MapEntryReports;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.SessionMetrics;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.UnansweredReports;

/// <summary>What the UnansweredReports slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class UnansweredReportsRegistration
{
    internal static IServiceCollection AddUnansweredReportsSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<CharacterStatisticDecoder>(
            GameProtocol.CharacterStatisticType, ClientMessageType.GameCharacterStatistic);
        services.AddGameCmdMessageDecoder<RequestBlacklistDecoder>(
            GameProtocol.RequestBlacklistType, ClientMessageType.GameRequestBlacklist);
        services.AddGameCmdMessageDecoder<FarmEventActionDecoder>(
            GameProtocol.FarmEventActionType, ClientMessageType.GameFarmEventAction);
        services.AddGameCmdMessageDecoder<PiDecoder>(
            GameProtocol.PiType, ClientMessageType.GamePi);
        services.AddGameCmdMessageDecoder<RequestIgaVersionDecoder>(
            GameProtocol.RequestIgaVersionType, ClientMessageType.GameRequestIgaVersion);
        services.AddGameCmdMessageDecoder<FpsPerformanceStatisticDecoder>(
            GameProtocol.FpsPerformanceStatisticType, ClientMessageType.GameFpsPerformanceStatistic);
        services.AddGameCmdMessageDecoder<FrameLagPerDungeonDecoder>(
            GameProtocol.FrameLagPerDungeonType, ClientMessageType.GameFrameLagPerDungeon);
        services.AddGameCmdMessageDecoder<LagStatisticsDecoder>(
            GameProtocol.LagStatisticsType, ClientMessageType.GameLagStatistics);
        services.AddGameCmdMessageDecoder<UseSkillDecoder>(
            GameProtocol.UseSkillType, ClientMessageType.GameUseSkill);
        services.AddGameCmdMessageDecoder<SyncItemSpaceDecoder>(
            GameProtocol.SyncItemSpaceType, ClientMessageType.GameSyncItemSpace);
        services.AddGameCmdMessageDecoder<OverflowInfoDecoder>(
            GameProtocol.OverflowInfoType, ClientMessageType.GameOverflowInfo);
        services.AddGameCmdMessageDecoder<LogDomainConnectDecoder>(
            GameProtocol.LogDomainConnectType, ClientMessageType.GameLogDomainConnect);
        services.AddGameCmdMessageDecoder<LogConnectProcessDecoder>(
            GameProtocol.LogConnectProcessType, ClientMessageType.GameLogConnectProcess);

        // Known reports that this server intentionally does not answer. A durability request is
        // not one of them: it changes a persisted equipment instance and has its own CMD reply.
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameUseSkill);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameSyncItemSpace);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameOverflowInfo);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameLogDomainConnect);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameLogConnectProcess);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameCharacterStatistic);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameRequestBlacklist);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameFarmEventAction);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GamePi);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameRequestIgaVersion);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameFpsPerformanceStatistic);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameFrameLagPerDungeon);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameLagStatistics);
        return services;
    }
}
