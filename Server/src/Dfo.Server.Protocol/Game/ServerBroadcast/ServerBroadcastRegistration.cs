using Dfo.Server.Abstractions.ServerBroadcast;
using Dfo.Server.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ServerBroadcast;

/// <summary>What the ServerBroadcast slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class ServerBroadcastRegistration
{
    internal static IServiceCollection AddServerBroadcastSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<BoxRewardBroadcast, ServerBroadcastEncoder<BoxRewardBroadcast>>();
        services.AddGameNotiMessageEncoder<SecondAwakeningBroadcast, ServerBroadcastEncoder<SecondAwakeningBroadcast>>();
        services.AddNotificationHandler<ServerBroadcastPosted, ServerBroadcastPostedHandler>();
        return services;
    }
}
