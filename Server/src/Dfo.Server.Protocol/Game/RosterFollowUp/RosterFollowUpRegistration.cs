using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.RosterFollowUp;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.RosterFollowUp;

/// <summary>What the RosterFollowUp slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class RosterFollowUpRegistration
{
    internal static IServiceCollection AddRosterFollowUpSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<RosterFollowUpMessageDecoder>(ClientMessageType.GameRosterFollowUp);
        services.AddGameCmdMessageEncoder<RosterFollowUpResponse, RosterFollowUpResponseEncoder>();
        services.AddProtocolCommandHandler<GameRosterFollowUpCommand, bool, GameRosterFollowUpCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameRosterFollowUp, typeof(RosterFollowUpRequest)));
        services.RegisterProtocolMessageHandler<RosterFollowUpHandler, RosterFollowUpRequest, GameRosterFollowUpCommand>(ClientMessageType.GameRosterFollowUp);
        return services;
    }
}
