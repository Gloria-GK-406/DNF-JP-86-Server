using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.Altar;
using Dfo.Server.Protocol.Game.RoomMechanisms;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol;

internal static class WorldMaintenanceCommandRegistration
{
    internal static IServiceCollection AddIsolatedWorldMaintenanceCommands(this IServiceCollection services)
    {
        services.AddInternalCommandHandler<MaintainAltarCommand, AltarNextDeadline, AltarTimerCommandHandler>(internalActionCancellation: InternalActionCancellationCapability.ConfirmedRollbackWriteResult);
        services.AddInternalCommandHandler<AdvanceAltarDeadlineCommand, bool, AltarTimerCommandHandler>(internalActionCancellation: InternalActionCancellationCapability.ConfirmedRollbackWriteResult);
        services.AddInternalCommandHandler<MaintainRoomMechanismsCommand, RoomMechanismNextDeadline, MaintainRoomMechanismsCommandHandler>();
        services.AddInternalCommandHandler<ProjectRoomMechanismCommand, bool, MaintainRoomMechanismsCommandHandler>();
        return services;
    }
}
