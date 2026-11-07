using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.EquipmentGrowth;

/// <summary>What the EquipmentGrowth slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class EquipmentGrowthRegistration
{
    internal static IServiceCollection AddEquipmentGrowthSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<GrowthItemRenewNotice, GrowthItemRenewNoticeEncoder>();
        services.AddGameNotiMessageEncoder<GrowthItemChangeNotice, GrowthItemChangeNoticeEncoder>();
        return services;
    }
}
