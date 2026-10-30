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
        services.AddGameCmdMessageDecoder<CharacterStatisticDecoder>(ClientMessageType.GameCharacterStatistic);
        services.AddGameCmdMessageDecoder<RequestBlacklistDecoder>(ClientMessageType.GameRequestBlacklist);
        services.AddGameCmdMessageDecoder<FarmEventActionDecoder>(ClientMessageType.GameFarmEventAction);
        services.AddGameCmdMessageDecoder<PiDecoder>(ClientMessageType.GamePi);
        services.AddGameCmdMessageDecoder<RequestIgaVersionDecoder>(ClientMessageType.GameRequestIgaVersion);
        services.AddGameCmdMessageDecoder<FpsPerformanceStatisticDecoder>(ClientMessageType.GameFpsPerformanceStatistic);
        services.AddGameCmdMessageDecoder<FrameLagPerDungeonDecoder>(ClientMessageType.GameFrameLagPerDungeon);
        services.AddGameCmdMessageDecoder<LagStatisticsDecoder>(ClientMessageType.GameLagStatistics);
        services.AddGameCmdMessageDecoder<UseSkillDecoder>(ClientMessageType.GameUseSkill);
        services.AddGameCmdMessageDecoder<SyncItemSpaceDecoder>(ClientMessageType.GameSyncItemSpace);
        services.AddGameCmdMessageDecoder<OverflowInfoDecoder>(ClientMessageType.GameOverflowInfo);
        services.AddGameCmdMessageDecoder<LogDomainConnectDecoder>(ClientMessageType.GameLogDomainConnect);
        services.AddGameCmdMessageDecoder<LogConnectProcessDecoder>(ClientMessageType.GameLogConnectProcess);

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
