using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.AuraSkinSlot;

/// <summary>What the AuraSkinSlot slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class AuraSkinSlotRegistration
{
    internal static IServiceCollection AddAuraSkinSlotSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<AuraSkinSlotDecoder>(ClientMessageType.GameOpenAuraSkinSlot);
        services.AddGameCmdMessageEncoder<AuraSkinSlotResponse, AuraSkinSlotEncoder>();
        services.AddProtocolCommandHandler<GameOpenAuraSkinSlotCommand, bool, GameOpenAuraSkinSlotCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameOpenAuraSkinSlot, typeof(AuraSkinSlotRequest)));
        services.RegisterProtocolMessageHandler<AuraSkinSlotHandler, AuraSkinSlotRequest, GameOpenAuraSkinSlotCommand>(ClientMessageType.GameOpenAuraSkinSlot);
        return services;
    }
}
