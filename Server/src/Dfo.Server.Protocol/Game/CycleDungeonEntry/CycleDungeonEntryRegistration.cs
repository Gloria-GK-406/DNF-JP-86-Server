using Dfo.Server.Protocol.Game.CycleDungeonEntry;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.CycleDungeonEntry;

/// <summary>What the CycleDungeonEntry slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class CycleDungeonEntryRegistration
{
    internal static IServiceCollection AddCycleDungeonEntrySlice(this IServiceCollection services)
    {
        services.RegisterProtocolMessageHandler<PrepareCycleDungeonEntryHandler, PrepareCycleDungeonEntryRequest, GamePrepareCycleDungeonEntryCommand>(ClientMessageType.GamePrepareCycleDungeonEntry);
        services.AddProtocolCommandHandler<GamePrepareCycleDungeonEntryCommand, CycleDungeonPreparation, GamePrepareCycleDungeonEntryCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GamePrepareCycleDungeonEntry, typeof(PrepareCycleDungeonEntryRequest)));
        services.AddGameCmdMessageDecoder<PrepareCycleDungeonEntryDecoder>(ClientMessageType.GamePrepareCycleDungeonEntry);
        services.AddGameCmdMessageEncoder<PrepareCycleDungeonEntryResponse, PrepareCycleDungeonEntryEncoder>();
        return services;
    }
}
