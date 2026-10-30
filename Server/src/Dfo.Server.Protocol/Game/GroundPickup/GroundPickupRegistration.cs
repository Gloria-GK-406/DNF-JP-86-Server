using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CharacterName;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.HiddenCharacterInfo;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GroundPickup;

/// <summary>What the GroundPickup slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class GroundPickupRegistration
{
    internal static IServiceCollection AddGroundPickupSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<GroundPickupNotification, GroundPickupEncoder>();
        services.AddGameNotiMessageEncoder<
            GroundGoldPickupNotification, GroundGoldPickupEncoder>();
        services.AddGameCmdMessageDecoder<GetItemDecoder>(ClientMessageType.GameGetItem);
        services.AddGameCmdMessageEncoder<GetItemResponse, GetItemResponseEncoder>();
        // Taking a piece off a room's floor: the claim on the floor and the row in the bag are one
        // change, and a piece that left the floor without arriving anywhere is the thing to avoid.
        services.AddProtocolCommandHandler<GameGetItemCommand, bool, GameGetItemCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGetItem, typeof(GetItemRequest)));
        services.RegisterProtocolMessageHandler<GetItemHandler, GetItemRequest, GameGetItemCommand>(ClientMessageType.GameGetItem);
        return services;
    }
}
