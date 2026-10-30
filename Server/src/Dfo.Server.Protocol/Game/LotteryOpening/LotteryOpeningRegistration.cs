using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.LotteryOpening;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.LotteryOpening;

/// <summary>What the LotteryOpening slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class LotteryOpeningRegistration
{
    internal static IServiceCollection AddLotteryOpeningSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<LotteryOpeningDecoder>(ClientMessageType.GameLotteryOpening);
        services.AddGameCmdMessageEncoder<LotteryOpeningResponse, LotteryOpeningEncoder>();
        services.AddProtocolCommandHandler<GameLotteryOpeningCommand, bool, GameLotteryOpeningCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameLotteryOpening, typeof(LotteryOpeningRequest)));
        services.RegisterProtocolMessageHandler<LotteryOpeningHandler, LotteryOpeningRequest, GameLotteryOpeningCommand>(ClientMessageType.GameLotteryOpening);
        return services;
    }
}
