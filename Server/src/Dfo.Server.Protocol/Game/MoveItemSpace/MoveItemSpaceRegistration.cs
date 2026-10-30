using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CharacterName;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.UnansweredReports;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.HiddenCharacterInfo;
using Dfo.Server.Protocol.Game.MapEntryReports;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.MoveItemSpace;

/// <summary>What the MoveItemSpace slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class MoveItemSpaceRegistration
{
    internal static IServiceCollection AddMoveItemSpaceSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<MoveItemSpaceDecoder>(
            GameProtocol.MoveItemSpaceType, ClientMessageType.GameMoveItemSpace);
        services.AddGameCmdMessageEncoder<MoveItemSpaceResponse, MoveItemSpaceResponseEncoder>();
        // Both act on one character's own container, which the registry already keeps per
        // character; neither reads or writes anything the account owns, so neither takes it.
        // Transactional, and in the same change that broke the move into separate writes. Marking
        // it afterwards would leave a window in which a move was several writes with nothing to
        // settle them together -- a window every test would pass through green.
        services.AddProtocolCommandHandler<GameMoveItemSpaceCommand, bool, GameMoveItemSpaceCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameMoveItemSpace, typeof(MoveItemSpaceRequest)));
        services.RegisterProtocolMessageHandler<MoveItemSpaceHandler, MoveItemSpaceRequest, GameMoveItemSpaceCommand>(ClientMessageType.GameMoveItemSpace);
        return services;
    }
}
