using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

public sealed class GmPlayerSamplingHttpTests
{
    [Fact]
    public async Task OnlineListUsesOneCapturedSetForTotalAndItemsEvenWhenDisconnectOccursDuringDatabaseRead()
    {
        var barrier = new ReadBarrier();
        await using var host = await GmTestHost.StartAsync(configureServices: services =>
        {
            var existing = services.Single(service => service.ServiceType == typeof(IGmPlayerDirectoryPersistence));
            services.Remove(existing);
            services.AddSingleton<IGmPlayerDirectoryPersistence>(provider => new BarrierPersistence(
                (IGmPlayerDirectoryPersistence)ActivatorUtilities.CreateInstance(provider, existing.ImplementationType!), barrier));
        });
        await GmPlayerDirectoryHttpTests.SeedAsync(host); await host.LoginAsync(await host.CsrfAsync());
        var user = await GmPlayerDirectoryHttpTests.ConnectAsync(host, 40, 10011);
        var session = GmPlayerDirectoryHttpTests.Publish(host, user, 40, 40, 10011);
        barrier.Armed = true;
        var pending = GmPlayerDirectoryHttpTests.GetAsync(host, "characters?onlineOnly=true&pageSize=1");
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        GmPlayerDirectoryHttpTests.Cleanup(host, session);
        host.Services.GetRequiredService<IOnlineUserDomainService>().TryDisconnectConnection(user.Connection.ConnectionId);
        barrier.Release.TrySetResult();
        var page = await pending;
        Assert.Equal(1, page.GetProperty("total").GetInt32()); Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        Assert.True(page.GetProperty("items")[0].GetProperty("isOnline").GetBoolean());
        var refreshed = await GmPlayerDirectoryHttpTests.GetAsync(host, "characters?onlineOnly=true");
        Assert.Equal(0, refreshed.GetProperty("total").GetInt32()); Assert.Equal(0, refreshed.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task SessionInvalidatedWhileReadingPositionIsExcludedBeforePagination()
    {
        Action? duringRead = null;
        await using var host = await GmTestHost.StartAsync(configureServices: services =>
        {
            var existing = services.Single(service => service.ServiceType == typeof(ICharacterPositionDomainService));
            services.Remove(existing);
            services.AddSingleton<ICharacterPositionDomainService>(provider => new InterleavedPosition(
                (ICharacterPositionDomainService)ActivatorUtilities.CreateInstance(provider, existing.ImplementationType!), () => { var action = duringRead; duringRead = null; action?.Invoke(); }));
        });
        await GmPlayerDirectoryHttpTests.SeedAsync(host); await host.LoginAsync(await host.CsrfAsync());
        var user = await GmPlayerDirectoryHttpTests.ConnectAsync(host, 40, 10011);
        var session = GmPlayerDirectoryHttpTests.Publish(host, user, 40, 40, 10011);
        duringRead = () => { GmPlayerDirectoryHttpTests.Cleanup(host, session); host.Services.GetRequiredService<IOnlineUserDomainService>().TryDisconnectConnection(user.Connection.ConnectionId); };
        var page = await GmPlayerDirectoryHttpTests.GetAsync(host, "characters?onlineOnly=true");
        Assert.Equal(0, page.GetProperty("total").GetInt32()); Assert.Empty(page.GetProperty("items").EnumerateArray());
        var detail = await GmPlayerDirectoryHttpTests.GetAsync(host, "characters/40");
        Assert.False(detail.GetProperty("isOnline").GetBoolean()); Assert.Equal("saved", detail.GetProperty("position").GetProperty("source").GetString());
    }

    private sealed class ReadBarrier
    {
        public bool Armed { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class BarrierPersistence(IGmPlayerDirectoryPersistence inner, ReadBarrier barrier) : IGmPlayerDirectoryPersistence
    {
        public ValueTask<GmStoredPage<GmAccountRecord>> AccountsAsync(GmPlayerFilter filter, IReadOnlyCollection<int>? onlineIds, CancellationToken token) => inner.AccountsAsync(filter, onlineIds, token);
        public async ValueTask<GmStoredPage<GmCharacterRecord>> CharactersAsync(GmPlayerFilter filter, IReadOnlyCollection<int>? onlineIds, CancellationToken token)
        {
            if (barrier.Armed) { barrier.Armed = false; barrier.Entered.TrySetResult(); await barrier.Release.Task.WaitAsync(token); }
            return await inner.CharactersAsync(filter, onlineIds, token);
        }
        public ValueTask<(long Accounts, long Characters)> CountsAsync(CancellationToken token) => inner.CountsAsync(token);
    }
    private sealed class InterleavedPosition(ICharacterPositionDomainService inner, Action onRead) : ICharacterPositionDomainService
    {
        public CharacterPositionState? Find(CharacterId id) { var result = inner.Find(id); onRead(); return result; }
        public void Place(SelectedSessionIdentity session, CharacterPositionState position) => inner.Place(session, position);
        public CharacterPositionState CrossInto(SelectedSessionIdentity session, byte town, byte area, short x, short y) => inner.CrossInto(session, town, area, x, y);
        public CharacterPositionState StepTo(SelectedSessionIdentity session, short x, short y, byte direction) => inner.StepTo(session, x, y, direction);
        public bool ForgetExact(SelectedSessionIdentity session) => inner.ForgetExact(session);
    }
}
