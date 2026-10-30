using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.LevelUpTicket;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.LevelUpTicket;

/// <summary>What the LevelUpTicket slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class LevelUpTicketRegistration
{
    internal static IServiceCollection AddLevelUpTicketSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<LevelUpTicketDecoder>(ClientMessageType.GameLevelUpTicket);
        services.AddGameCmdMessageEncoder<LevelUpTicketResponse, LevelUpTicketEncoder>();
        services.AddProtocolCommandHandler<GameLevelUpTicketCommand, bool, GameLevelUpTicketCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameLevelUpTicket, typeof(LevelUpTicketRequest)));
        services.RegisterProtocolMessageHandler<LevelUpTicketHandler, LevelUpTicketRequest, GameLevelUpTicketCommand>(ClientMessageType.GameLevelUpTicket);
        return services;
    }
}
