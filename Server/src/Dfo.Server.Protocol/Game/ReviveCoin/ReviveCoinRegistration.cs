using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ReviveCoin;

/// <summary>What the ReviveCoin slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ReviveCoinRegistration
{
    internal static IServiceCollection AddReviveCoinSlice(this IServiceCollection services)
    {
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.ReviveCoin.DailyReviveCoinHandler, Dfo.Server.Protocol.Game.ReviveCoin.DailyReviveCoinRequest, Dfo.Server.Protocol.Game.ReviveCoin.GameDailyReviveCoinCommand>(ClientMessageType.GameDailyReviveCoin);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.ReviveCoin.DailyReviveCoinDecoder>(0x00CF, ClientMessageType.GameDailyReviveCoin);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.ReviveCoin.DailyReviveCoinResponse, Dfo.Server.Protocol.Game.ReviveCoin.DailyReviveCoinEncoder>();
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.ReviveCoin.GameDailyReviveCoinCommand, DailyReviveCoinResult, Dfo.Server.Protocol.Game.ReviveCoin.GameDailyReviveCoinCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameDailyReviveCoin, typeof(Dfo.Server.Protocol.Game.ReviveCoin.DailyReviveCoinRequest)));
        return services;
    }
}
