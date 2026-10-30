using Dfo.Server.Protocol.Game.IncreaseStatus;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.IncreaseStatus;

/// <summary>What the IncreaseStatus slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class IncreaseStatusRegistration
{
    internal static IServiceCollection AddIncreaseStatusSlice(this IServiceCollection services)
    {
        services.AddProtocolCommandHandler<GameIncreaseStatusCommand, IncreaseStatusResult, GameIncreaseStatusCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameIncreaseStatus, typeof(IncreaseStatusRequest)));
        services.RegisterProtocolMessageHandler<IncreaseStatusHandler, IncreaseStatusRequest, GameIncreaseStatusCommand>(ClientMessageType.GameIncreaseStatus);
        services.AddGameCmdMessageDecoder<IncreaseStatusDecoder>(0x001E, ClientMessageType.GameIncreaseStatus);
        services.AddGameCmdMessageEncoder<IncreaseStatusResponse, IncreaseStatusEncoder>();
        return services;
    }
}
