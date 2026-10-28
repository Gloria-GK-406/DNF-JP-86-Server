using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.Altar;
using Dfo.Server.Protocol.Game.RoomMechanisms;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol;

internal static class WorldMaintenanceCommandRegistration
{
    internal static IServiceCollection AddIsolatedWorldMaintenanceCommands(this IServiceCollection services)
    {
        services.AddInternalCommandHandler<Dfo.Server.Protocol.Game.GuardianAltar.ReturnGuardianAltarCommand, System.Collections.Immutable.ImmutableArray<Dfo.Server.Data.World.SelectedSessionIdentity>, Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarReturnCommandHandler>();
        services.AddInternalCommandHandler<Dfo.Server.Protocol.Game.GuardianAltar.MaintainGuardianAltarCommand, Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarSchedule, Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarTimerCommandHandler>(internalActionCancellation: InternalActionCancellationCapability.ConfirmedRollbackWriteResult);
        services.AddInternalCommandHandler<Dfo.Server.Protocol.Game.GuardianAltar.AdvanceGuardianAltarCommand, bool, Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarTimerCommandHandler>(internalActionCancellation: InternalActionCancellationCapability.ConfirmedRollbackWriteResult);
        services.AddInternalCommandHandler<MaintainAltarCommand, AltarNextDeadline, AltarTimerCommandHandler>(internalActionCancellation: InternalActionCancellationCapability.ConfirmedRollbackWriteResult);
        services.AddInternalCommandHandler<AdvanceAltarDeadlineCommand, bool, AltarTimerCommandHandler>(internalActionCancellation: InternalActionCancellationCapability.ConfirmedRollbackWriteResult);
        services.AddInternalCommandHandler<MaintainRoomMechanismsCommand, RoomMechanismNextDeadline, MaintainRoomMechanismsCommandHandler>();
        services.AddInternalCommandHandler<ProjectRoomMechanismCommand, bool, MaintainRoomMechanismsCommandHandler>();
        return services;
    }
}
