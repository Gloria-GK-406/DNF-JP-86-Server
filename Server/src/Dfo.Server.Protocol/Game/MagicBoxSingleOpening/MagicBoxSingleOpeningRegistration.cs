using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MagicBoxSingleOpening;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.MagicBoxSingleOpening;

/// <summary>What the MagicBoxSingleOpening slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class MagicBoxSingleOpeningRegistration
{
    internal static IServiceCollection AddMagicBoxSingleOpeningSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<MagicBoxSingleOpeningDecoder>(0x00D0, ClientMessageType.GameMagicBoxSingleOpening);
        services.AddGameCmdMessageEncoder<MagicBoxSingleOpeningResponse, MagicBoxSingleOpeningEncoder>();
        services.AddProtocolCommandHandler<GameMagicBoxSingleOpeningCommand, MagicBoxOpeningResult, GameMagicBoxSingleOpeningCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameMagicBoxSingleOpening, typeof(MagicBoxSingleOpeningRequest)));
        services.RegisterProtocolMessageHandler<MagicBoxSingleOpeningHandler, MagicBoxSingleOpeningRequest, GameMagicBoxSingleOpeningCommand>(ClientMessageType.GameMagicBoxSingleOpening);
        return services;
    }
}
