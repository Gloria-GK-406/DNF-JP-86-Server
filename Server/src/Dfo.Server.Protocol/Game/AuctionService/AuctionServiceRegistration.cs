using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.AuctionService;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.AuctionService;

/// <summary>What the AuctionService slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class AuctionServiceRegistration
{
    internal static IServiceCollection AddAuctionServiceSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<AuctionServiceStateNotice, AuctionServiceStateNoticeEncoder>();
        return services;
    }
}
