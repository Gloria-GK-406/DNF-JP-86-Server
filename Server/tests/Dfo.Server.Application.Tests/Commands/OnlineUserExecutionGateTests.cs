using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Commands;

public sealed class OnlineUserExecutionGateTests
{
    [Fact]
    public async Task DifferentOnlineUsersDoNotBlockEachOther()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var gate = new OnlineUserExecutionGate();
            await using var first = await gate.AcquireAsync(
                OnlineUserId.New(), UserAccessMode.Exclusive, CancellationToken.None);

            await using var second = await gate.AcquireAsync(
                OnlineUserId.New(), UserAccessMode.Exclusive, CancellationToken.None);
        });
    }
}
