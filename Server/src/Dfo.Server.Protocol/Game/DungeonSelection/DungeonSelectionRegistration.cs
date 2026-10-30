using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.DungeonSelection;

/// <summary>What the DungeonSelection slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class DungeonSelectionRegistration
{
    internal static IServiceCollection AddDungeonSelectionSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<EnterSelectDungeonDecoder>(
            GameProtocol.EnterSelectDungeonType, ClientMessageType.GameEnterSelectDungeon);
        services.AddGameCmdMessageDecoder<BackToVillageDecoder>(
            GameProtocol.BackToVillageType, ClientMessageType.GameBackToVillage);
        services.AddGameNotiMessageEncoder<UserAreaNotification, UserAreaEncoder>();
        services.AddGameNotiMessageEncoder<UserStateNotification, UserStateEncoder>();
        services.AddGameNotiMessageEncoder<UdpHostNotification, UdpHostEncoder>();

        services.AddGameCmdMessageEncoder<EnterSelectDungeonAcknowledgement, EnterSelectDungeonAcknowledgementEncoder>();

        services.AddGameNotiMessageEncoder<EnterSelectDungeonNotification, EnterSelectDungeonEncoder>();
        services.AddGameCmdMessageEncoder<BackToVillageResponse, BackToVillageResponseEncoder>();
        services.AddProtocolCommandHandler<GameEnterSelectDungeonCommand, bool, GameDungeonSelectionCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameEnterSelectDungeon, typeof(EnterSelectDungeonRequest)));
        services.RegisterProtocolMessageHandler<EnterSelectDungeonHandler, EnterSelectDungeonRequest, GameEnterSelectDungeonCommand>(ClientMessageType.GameEnterSelectDungeon);
        services.AddProtocolCommandHandler<GameBackToVillageCommand, bool, GameDungeonSelectionCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameBackToVillage, typeof(BackToVillageRequest)));
        services.RegisterProtocolMessageHandler<BackToVillageHandler, BackToVillageRequest, GameBackToVillageCommand>(ClientMessageType.GameBackToVillage);
        return services;
    }
}
