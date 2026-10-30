using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CompoundItem;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.CompoundItem;

/// <summary>What the CompoundItem slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class CompoundItemRegistration
{
    internal static IServiceCollection AddCompoundItemSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<CompoundItemDecoder>(GameProtocol.CompoundItemType, ClientMessageType.GameCompoundItem);
        services.AddGameCmdMessageEncoder<CompoundItemResponse, CompoundItemEncoder>();
        services.AddProtocolCommandHandler<GameCompoundItemCommand, bool, GameCompoundItemCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameCompoundItem, typeof(CompoundItemRequest)));
        services.RegisterProtocolMessageHandler<CompoundItemHandler, CompoundItemRequest, GameCompoundItemCommand>(ClientMessageType.GameCompoundItem);
        return services;
    }
}
