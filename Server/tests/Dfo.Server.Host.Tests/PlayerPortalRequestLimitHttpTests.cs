using System.Net;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.GmWeb;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Host.Tests.PlayerPortalTestSupport;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// CH-1: one account's player requests may hold at most <see cref="PlayerRequestLimiter.PerAccount"/> places on the
/// gameplay executor. The next one is refused before it is queued (never run, never audited); the places come back
/// when the requests finish; other accounts and the off-executor catalog routes are not affected.
/// </summary>
public sealed class PlayerPortalRequestLimitHttpTests
{
    private const string MePath = "/api/player/v1/me";

    [Fact]
    public async Task Requests_over_the_per_account_cap_are_refused_unqueued_and_the_places_return_when_they_finish()
    {
        await using var host = await GmTestHost.StartAsync();
        await SeedAccountAsync(host, 1, "alice"); await SeedAccountAsync(host, 2, "bob");
        await SetPortalAsync(host);
        using var alice = new PlayerClient(host); var csrf = await alice.SignInAsync("alice");
        using var bob = new PlayerClient(host); await bob.SignInAsync("bob");
        var limiter = host.Services.GetRequiredService<PlayerRequestLimiter>();
        Assert.Equal(2, PlayerRequestLimiter.PerAccount);

        // Hold the executor so that the player's CMDs stay queued.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = host.Services.GetRequiredService<IGameplayQueue>().RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Gm), "test:hold-executor", async _ => { started.SetResult(); await gate.Task; }), default).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<HttpResponseMessage>[] held = [];
        try
        {
            held = [alice.Client.GetAsync(MePath), alice.Client.GetAsync(MePath)];
            await UntilAsync(() => limiter.InFlight(1) == 2);

            var requestId = Guid.NewGuid();
            using (var refused = await alice.GrantAsync(csrf, "Cera", 5, requestId))
            {
                Assert.Equal((HttpStatusCode)429, refused.StatusCode);
                var body = await JsonAsync(refused);
                Assert.Equal("rate_limited", body.GetProperty("code").GetString());
                Assert.False(body.TryGetProperty("outcome", out _));
            }
            using (var refused = await alice.Client.GetAsync(MePath))
                Assert.Equal("rate_limited", await CodeAsync(refused));
            Assert.Equal(2, limiter.InFlight(1));

            // Off the executor and another account's places are untouched.
            using (var icons = await alice.Client.GetAsync("/api/player/v1/items/icon-pack"))
                Assert.Equal(HttpStatusCode.OK, icons.StatusCode);
            using (var session = await alice.Client.GetAsync("/api/player/v1/auth/session"))
                Assert.Equal(HttpStatusCode.OK, session.StatusCode);
            held = [.. held, bob.Client.GetAsync(MePath)];
            await UntilAsync(() => limiter.InFlight(2) == 1);

            gate.SetResult();
            await blocker.WaitAsync(TimeSpan.FromSeconds(10));
            foreach (var response in await Task.WhenAll(held).WaitAsync(TimeSpan.FromSeconds(30)))
                using (response) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, limiter.InFlight(1)); Assert.Equal(0, limiter.InFlight(2));

            // The refused grant never ran: nothing was written or audited, and its request ID is still unused.
            Assert.Equal("10:20", await BalanceAsync(host, 1));
            Assert.Equal(["0"], await host.ReadAsync("SELECT COUNT(*) FROM gm_audit WHERE action='PlayerGrantCera';"));
            using var granted = await alice.GrantAsync(csrf, "Cera", 5, requestId);
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
            Assert.False((await JsonAsync(granted)).GetProperty("replayed").GetBoolean());
            Assert.Equal("15:20", await BalanceAsync(host, 1));
            Assert.Equal(0, limiter.InFlight(1));
        }
        finally
        {
            gate.TrySetResult();
            foreach (var response in held)
                try { (await response.WaitAsync(TimeSpan.FromSeconds(30))).Dispose(); } catch (Exception error) when (error is HttpRequestException or TimeoutException) { }
        }
    }

    [Fact]
    public void A_place_is_released_once_and_never_below_zero()
    {
        var limiter = new PlayerRequestLimiter();
        Assert.True(limiter.TryEnter(7)); Assert.True(limiter.TryEnter(7)); Assert.False(limiter.TryEnter(7));
        Assert.True(limiter.TryEnter(8));
        limiter.Exit(7); Assert.Equal(1, limiter.InFlight(7)); Assert.True(limiter.TryEnter(7));
        limiter.Exit(7); limiter.Exit(7); limiter.Exit(7);
        Assert.Equal(0, limiter.InFlight(7)); Assert.Equal(1, limiter.InFlight(8));
        Assert.True(limiter.TryEnter(7)); Assert.True(limiter.TryEnter(7)); Assert.False(limiter.TryEnter(7));
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The requests did not reach the executor queue.");
            await Task.Delay(10);
        }
    }
}
