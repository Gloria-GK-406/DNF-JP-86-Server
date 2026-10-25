using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Support;

// Keep the original semantic assertions while entering through the production dispatcher.
internal static class LightweightMessageDispatch
{
    public static async ValueTask<IReadOnlyList<byte[]>> Send<T>(NetworkDispatchContext context, IClientNetworkMessage message,
        IOnlineUserQueryDomainService users, ILogger<T> logger)
    {
        using var fixture = new ShopFixture(services =>
        {
            services.AddSingleton(users);
            services.AddSingleton(logger);
        });
        await fixture.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(context, message, default);
        return fixture.Sender.Outbound.Frames.ToArray();
    }
}
