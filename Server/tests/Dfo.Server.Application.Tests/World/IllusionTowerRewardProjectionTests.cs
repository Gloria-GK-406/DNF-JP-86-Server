using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class IllusionTowerRewardProjectionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Committed_kill_batch_retries_once_without_repeating_assets(int floors)
    {
        var sender = new FailingSender { FailuresRemaining = 1 };
        var closer = new CleanupCloser();
        using var fixture = Fixture(floors, sender, closer);
        var run = await Start(fixture);
        var states = new List<(long Experience, long Gold, long Items)>();
        sender.OnAttempt = () => states.Add(Assets(fixture));

        Assert.True(await Delivery(fixture).KillAsync(fixture.Session.ConnectionId,
            GameMessageMetadata.Empty, run.Room.Actors[0].Uid, fixture.Session.WireUserId, default));

        Assert.Equal(2, sender.Attempts.Count);
        Assert.Same(sender.Attempts[0], sender.Attempts[1]);
        Assert.Equal(states[0], states[1]);
        Assert.Equal(states[0], Assets(fixture));
        Assert.True(states[0].Experience > 0);
        Assert.Empty(closer.Closed);
        var current = Tower(fixture).Find(fixture.Session)!;
        Assert.Equal(floors == 1, current.Completion?.Settled == true);
        if (floors == 1)
        {
            Assert.True(states[0].Gold > 100000);
            Assert.True(states[0].Items > 0);
            Assert.Contains(sender.Attempts[1].RequiredDirectMessages,
                message => message.GetType().Name == "IllusionTowerRewardNotification");
        }

        Assert.True(await Delivery(fixture).KillAsync(fixture.Session.ConnectionId,
            GameMessageMetadata.Empty, run.Room.Actors[0].Uid, fixture.Session.WireUserId, default));
        Assert.Equal(2, sender.Attempts.Count);
        Assert.Equal(states[0], Assets(fixture));
    }

    [Fact]
    public async Task Persistent_delivery_failure_closes_after_two_attempts_and_real_cleanup_removes_receipt()
    {
        var sender = new FailingSender { FailuresRemaining = int.MaxValue };
        var closer = new CleanupCloser();
        using var fixture = Fixture(1, sender, closer);
        var session = fixture.Session;
        var run = await Start(fixture);
        var states = new List<(long Experience, long Gold, long Items)>();
        sender.OnAttempt = () => states.Add(Assets(fixture));
        closer.OnClose = async connectionId =>
        {
            _ = await fixture.Services.GetRequiredService<ISelectedSessionCleanupDomainService>()
                .CleanupAsync(session, includeOwner: false, default);
        };

        Assert.True(await Delivery(fixture).KillAsync(session.ConnectionId,
            GameMessageMetadata.Empty, run.Room.Actors[0].Uid, session.WireUserId, default));

        Assert.Equal(2, sender.Attempts.Count);
        Assert.Same(sender.Attempts[0], sender.Attempts[1]);
        Assert.Equal([session.ConnectionId], closer.Closed);
        Assert.Null(Tower(fixture).Find(session));
        Assert.Empty(Tower(fixture).FindReturnsDue(DateTimeOffset.MaxValue));
        Assert.Null(fixture.Services.GetRequiredService<ISelectedSessionDomainService>()
            .FindByCharacter(session.CharacterId));
        Assert.Equal(states[0], states[1]);
        Assert.Equal(states[0], Assets(fixture));
    }

    [Fact]
    public async Task A_replacement_session_receives_neither_old_retry_nor_close_and_survives_old_cleanup()
    {
        var sender = new FailingSender { FailuresRemaining = 1 };
        var closer = new CleanupCloser();
        using var fixture = Fixture(1, sender, closer);
        var old = fixture.Session;
        var run = await Start(fixture);
        SelectedSessionIdentity? replacement = null;
        sender.OnAttempt = () => replacement ??= ReplaceSession(fixture);

        Assert.True(await Delivery(fixture).KillAsync(old.ConnectionId,
            GameMessageMetadata.Empty, run.Room.Actors[0].Uid, old.WireUserId, default));

        Assert.Single(sender.Attempts);
        Assert.Empty(closer.Closed);
        Assert.NotNull(replacement);
        var cleanup = fixture.Services.GetRequiredService<ISelectedSessionCleanupDomainService>();
        _ = await cleanup.CleanupAsync(old, includeOwner: false, default);
        Assert.Null(Tower(fixture).Find(old));
        Assert.Equal(replacement, fixture.Services.GetRequiredService<ISelectedSessionDomainService>()
            .FindByCharacter(old.CharacterId));
        var next = await Tower(fixture).EnterAsync([new(replacement, 0)], replacement, 43, default);
        Assert.NotNull(next.Run);
        Assert.NotEqual(run.RunId, next.Run.RunId);
        _ = await cleanup.CleanupAsync(old, includeOwner: false, default);
        Assert.Equal(next.Run.RunId, Tower(fixture).Find(replacement)!.RunId);
        Assert.Single(sender.Attempts);
        Assert.Empty(closer.Closed);
    }

    private static ShopFixture Fixture(int floors, FailingSender sender, CleanupCloser closer)
    {
        var content = new TowerRewardTestContent();
        content.Table = content.Table with { ItemTypeRates = [0, 10000, 0, 0] };
        var fixture = new ShopFixture(services =>
        {
            TowerRewardTestContent.Register(services, content);
            services.AddSingleton<IIllusionTowerRuleService>(new IllusionTowerDomainServiceTests.Rules
                { FloorCount = floors, CardCount = 10, Probabilities = [350, 651, 0] });
            services.AddSingleton<IGameMessageSender>(sender);
            services.AddSingleton<INetworkConnectionCloser>(closer);
        });
        fixture.Sql($"UPDATE characters SET level=50,exp={fixture.ExperienceForLevel(50)};");
        return fixture;
    }

    private static async Task<IllusionTowerSnapshot> Start(ShopFixture fixture)
    {
        await fixture.Seed(65, 4183, 3);
        var tower = Tower(fixture);
        var run = (await tower.EnterAsync([new(fixture.Session, 2)], fixture.Session, 42, default)).Run!;
        var context = new TowerActionContext(fixture.Session, run.RunId, run.Room.Version);
        await tower.LoadAsync(context, default);
        await tower.StartAsync(context, default);
        return tower.Find(fixture.Session)!;
    }

    private static (long Experience, long Gold, long Items) Assets(ShopFixture fixture) =>
        (fixture.Scalar("SELECT exp FROM characters WHERE character_id=1"), fixture.Gold, fixture.Count(3000));

    private static IIllusionTowerDomainService Tower(ShopFixture fixture) =>
        fixture.Services.GetRequiredService<IIllusionTowerDomainService>();

    private static IllusionTowerProjectionDelivery Delivery(ShopFixture fixture) =>
        fixture.Services.GetRequiredService<IllusionTowerProjectionDelivery>();

    private static SelectedSessionIdentity ReplaceSession(ShopFixture fixture)
    {
        var registry = fixture.Services.GetRequiredService<OnlineStateRegistry>();
        var connection = new NetworkConnectionContext(new("tower-retry-replacement"), NetworkEndpointKind.Game, 10011);
        var user = registry.RegisterConnection(connection).OnlineUserId;
        registry.Authenticate(user, fixture.Session.AccountId);
        var replacement = SelectedSessionIdentity.Create(fixture.Session.AccountId, fixture.Session.CharacterId,
            connection.ConnectionId, registry.GetRequiredUser(user).Generation, connection.ListenerPort);
        registry.Select(replacement);
        registry.EnterWorld(replacement);
        fixture.Services.GetRequiredService<ISelectedSessionDomainService>().Select(replacement);
        return replacement;
    }

    private sealed class FailingSender : IGameMessageSender
    {
        public int FailuresRemaining { get; set; }
        public Action? OnAttempt { get; set; }
        public List<GameExplicitMessageDelivery> Attempts { get; } = [];

        public ValueTask SendExplicitAsync(NetworkConnectionId connectionId,
            GameExplicitMessageDelivery delivery, CancellationToken token)
        {
            Attempts.Add(delivery);
            OnAttempt?.Invoke();
            if (FailuresRemaining-- > 0)
                throw new IOException("Injected committed-batch delivery failure.");
            return ValueTask.CompletedTask;
        }

        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message,
            CancellationToken token) => throw new NotSupportedException();

        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class CleanupCloser : INetworkConnectionCloser
    {
        public List<NetworkConnectionId> Closed { get; } = [];
        public Func<NetworkConnectionId, ValueTask>? OnClose { get; set; }

        public async ValueTask RequestCloseAsync(NetworkConnectionId connectionId, CancellationToken token)
        {
            Closed.Add(connectionId);
            if (OnClose is not null) await OnClose(connectionId);
        }

        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken token) =>
            RequestCloseAsync(expected.ConnectionId, token);
    }
}
