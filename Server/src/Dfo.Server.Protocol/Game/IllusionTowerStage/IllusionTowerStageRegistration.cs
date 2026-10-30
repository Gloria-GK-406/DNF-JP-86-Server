using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.IllusionTowerStage;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.IllusionTowerStage;

/// <summary>What the IllusionTowerStage slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class IllusionTowerStageRegistration
{
    internal static IServiceCollection AddIllusionTowerStageSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<IllusionTowerStageDecoder>(
            GameProtocol.IllusionTowerStageType, ClientMessageType.GameIllusionTowerStage);
        services.AddProtocolCommandHandler<GameIllusionTowerStageCommand, bool, GameMapLoadingCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameIllusionTowerStage, typeof(IllusionTowerStageRequest)));
        services.RegisterProtocolMessageHandler<IllusionTowerStageHandler, IllusionTowerStageRequest, GameIllusionTowerStageCommand>(ClientMessageType.GameIllusionTowerStage);
        return services;
    }
}
