using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MagicBoxBatchOpening;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.MagicBoxBatchOpening;

/// <summary>What the MagicBoxBatchOpening slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class MagicBoxBatchOpeningRegistration
{
    internal static IServiceCollection AddMagicBoxBatchOpeningSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<MagicBoxBatchOpeningDecoder>(0x0450, ClientMessageType.GameMagicBoxBatchOpening);
        services.AddGameCmdMessageEncoder<MagicBoxBatchOpeningResponse, MagicBoxBatchOpeningEncoder>();
        services.AddProtocolCommandHandler<GameMagicBoxBatchOpeningCommand, MagicBoxOpeningResult, GameMagicBoxBatchOpeningCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameMagicBoxBatchOpening, typeof(MagicBoxBatchOpeningRequest)));
        services.RegisterProtocolMessageHandler<MagicBoxBatchOpeningHandler, MagicBoxBatchOpeningRequest, GameMagicBoxBatchOpeningCommand>(ClientMessageType.GameMagicBoxBatchOpening);
        return services;
    }
}
