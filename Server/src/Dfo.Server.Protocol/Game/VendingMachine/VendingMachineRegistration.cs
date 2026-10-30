using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.VendingMachine;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.VendingMachine;

/// <summary>What the VendingMachine slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class VendingMachineRegistration
{
    internal static IServiceCollection AddVendingMachineSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<VendingMachineDecoder>(0x00D7, ClientMessageType.GameVendingMachine);
        services.AddGameCmdMessageEncoder<VendingMachineResponse, VendingMachineEncoder>();
        services.AddProtocolCommandHandler<GameVendingMachineCommand, bool, GameVendingMachineCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameVendingMachine, typeof(VendingMachineRequest)));
        services.RegisterProtocolMessageHandler<VendingMachineHandler, VendingMachineRequest, GameVendingMachineCommand>(ClientMessageType.GameVendingMachine);
        return services;
    }
}
