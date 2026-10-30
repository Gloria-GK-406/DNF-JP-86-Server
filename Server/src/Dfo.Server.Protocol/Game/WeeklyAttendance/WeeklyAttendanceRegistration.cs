using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.WeeklyAttendance;

/// <summary>What the WeeklyAttendance slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class WeeklyAttendanceRegistration
{
    internal static IServiceCollection AddWeeklyAttendanceSlice(this IServiceCollection services)
    {
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceHandler, Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceRequest, Dfo.Server.Protocol.Game.WeeklyAttendance.GameWeeklyAttendanceCommand>(ClientMessageType.GameWeeklyAttendance);
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.WeeklyAttendance.GameWeeklyAttendanceCommand, bool, Dfo.Server.Protocol.Game.WeeklyAttendance.GameWeeklyAttendanceCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameWeeklyAttendance, typeof(Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceRequest)));
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceDecoder>(0x0441,ClientMessageType.GameWeeklyAttendance);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceResponse,Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceResponseEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.EventInformationMessage,GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.EventInformationMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceStateMessage,GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceStateMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.ScheduledDungeonStateMessage,GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.ScheduledDungeonStateMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.ScheduledActivityStateMessage,GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.ScheduledActivityStateMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.ScheduledQuestStateMessage,GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.ScheduledQuestStateMessage>>();
        services.AddNotificationHandler<Dfo.Server.Abstractions.Events.EventsChanged,Dfo.Server.Protocol.Game.WeeklyAttendance.EventsChangedHandler>();
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.WeeklyAttendance.AttendanceCompleted,Dfo.Server.Protocol.Game.WeeklyAttendance.AttendanceCompletedHandler>();
        return services;
    }
}
