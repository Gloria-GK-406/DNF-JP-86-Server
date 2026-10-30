using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.GenCeraTicket;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GenCeraTicket;

/// <summary>What the GenCeraTicket slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class GenCeraTicketRegistration
{
    internal static IServiceCollection AddGenCeraTicketSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GenCeraTicketMessageDecoder>(0x0041, ClientMessageType.GameGenCeraTicket);
        services.AddGameCmdMessageEncoder<GenCeraTicketUnavailable, GenCeraTicketResponseEncoder>();
        services.AddProtocolCommandHandler<GameGenCeraTicketCommand, bool, GameGenCeraTicketCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGenCeraTicket, typeof(GenCeraTicketRequest)));
        services.RegisterProtocolMessageHandler<GenCeraTicketHandler, GenCeraTicketRequest, GameGenCeraTicketCommand>(ClientMessageType.GameGenCeraTicket);
        return services;
    }
}
