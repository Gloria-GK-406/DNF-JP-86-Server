using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.HonorLevel;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.HonorLevel;

/// <summary>What the HonorLevel slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class HonorLevelRegistration
{
    internal static IServiceCollection AddHonorLevelSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<HonorLevelInfoNotice, HonorLevelInfoNoticeEncoder>();
        return services;
    }
}
