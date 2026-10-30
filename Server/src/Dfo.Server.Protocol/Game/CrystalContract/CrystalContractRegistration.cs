using Dfo.Server.Protocol.Game.CrystalContract;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.CrystalContract;

/// <summary>What the CrystalContract slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class CrystalContractRegistration
{
    internal static IServiceCollection AddCrystalContractSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<CrystalContractSelected, CrystalContractSelectedHandler>();
        services.AddGameCmdMessageDecoder<CrystalContractDecoder>(ClientMessageType.GameCrystalContract);
        services.AddGameCmdMessageEncoder<CrystalContractResponse, CrystalContractEncoder>();
        services.AddProtocolCommandHandler<GameCrystalContractCommand, bool, GameCrystalContractCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCrystalContract, typeof(CrystalContractRequest)));
        services.RegisterProtocolMessageHandler<CrystalContractHandler, CrystalContractRequest, GameCrystalContractCommand>(ClientMessageType.GameCrystalContract);
        return services;
    }
}
