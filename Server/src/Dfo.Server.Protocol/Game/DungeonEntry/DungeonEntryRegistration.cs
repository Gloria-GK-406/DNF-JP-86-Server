using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.DungeonEntry;

/// <summary>What the DungeonEntry slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class DungeonEntryRegistration
{
    internal static IServiceCollection AddDungeonEntrySlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<SelectDungeonDecoder>(ClientMessageType.GameSelectDungeon);
        services.AddGameCmdMessageDecoder<MoveMapDecoder>(ClientMessageType.GameMoveMap);
        services.AddGameNotiMessageEncoder<DungeonInfoNotification, DungeonInfoEncoder>();
        services.AddGameNotiMessageEncoder<StartMapNotification, StartMapEncoder>();
        services.AddGameNotiMessageEncoder<SupportTagNotification, SupportTagEncoder>();
        services.AddGameCmdMessageEncoder<SelectDungeonRefusal, SelectDungeonRefusalEncoder>();
        services.AddProtocolCommandHandler<GameSelectDungeonCommand, bool, GameSelectDungeonCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSelectDungeon, typeof(SelectDungeonRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameMirrorDungeonEntry, typeof(SelectDungeonRequest)));
        services.RegisterProtocolMessageHandler<SelectDungeonHandler, SelectDungeonRequest, GameSelectDungeonCommand>(ClientMessageType.GameSelectDungeon);
        services.RegisterProtocolMessageHandler<SelectDungeonHandler, SelectDungeonRequest, GameSelectDungeonCommand>(ClientMessageType.GameMirrorDungeonEntry);
        services.AddProtocolCommandHandler<GameMoveMapCommand, bool, GameMapLoadingCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameMoveMap, typeof(MoveMapRequest)));
        services.RegisterProtocolMessageHandler<MoveMapHandler, MoveMapRequest, GameMoveMapCommand>(ClientMessageType.GameMoveMap);
        return services;
    }
}
