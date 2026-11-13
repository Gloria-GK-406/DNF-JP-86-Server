using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Events;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Xunit;

namespace Dfo.Server.Application.Tests.Events;

public sealed class OnlineTimeRewardTests
{
    [Fact]
    public async Task The_first_round_starts_the_clock_and_a_full_period_pays_once()
    {
        var world = new World(new(true, TimeSpan.FromMinutes(5), 10, 30, true));
        var session = world.Enter(1, "c1");

        Assert.False(await world.AccrueAsync(session));
        world.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.False(await world.AccrueAsync(session));
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await world.AccrueAsync(session));
        Assert.False(await world.AccrueAsync(session));

        Assert.Equal(new AccountCeraBalances(10, 30, 0), world.Wallets.Balance(session.AccountId));
        var paid = Assert.Single(world.Events.Enqueued.OfType<OnlineTimeRewardGranted>());
        Assert.Equal(new OnlineTimeRewardGranted(session.AccountId, session.CharacterId, session.ConnectionId.Value,
            session.ConnectionGeneration, 10, 30, true), paid);
    }

    [Fact]
    public async Task A_long_gap_pays_one_period_not_every_missed_one()
    {
        var world = new World(new(true, TimeSpan.FromMinutes(5), 0, 7, false));
        var session = world.Enter(1, "c1");
        await world.AccrueAsync(session);

        world.Clock.Advance(TimeSpan.FromMinutes(23));
        Assert.True(await world.AccrueAsync(session));
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(await world.AccrueAsync(session));
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await world.AccrueAsync(session));

        Assert.Equal(14, world.Wallets.Balance(session.AccountId).TokenCera);
    }

    [Fact]
    public async Task A_session_that_left_the_world_starts_over_when_it_returns()
    {
        var world = new World(new(true, TimeSpan.FromMinutes(5), 1, 0, false));
        var first = world.Enter(1, "c1");
        await world.AccrueAsync(first);
        world.Clock.Advance(TimeSpan.FromMinutes(4));

        world.Leave(first);
        world.Retain();
        var second = world.Enter(1, "c2");
        Assert.False(await world.AccrueAsync(second));
        world.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.False(await world.AccrueAsync(second));
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await world.AccrueAsync(second));
        Assert.False(await world.AccrueAsync(first));

        Assert.Equal(1, world.Wallets.Balance(second.AccountId).Cera);
    }

    [Fact]
    public async Task A_currency_at_its_limit_is_skipped_and_the_other_is_still_paid()
    {
        var world = new World(new(true, TimeSpan.FromMinutes(1), 5, 5, true));
        var session = world.Enter(1, "c1");
        world.Wallets.Set(session.AccountId, new(int.MaxValue, 0, 0));
        await world.AccrueAsync(session);
        world.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(await world.AccrueAsync(session));

        Assert.Equal(new AccountCeraBalances(int.MaxValue, 5, 0), world.Wallets.Balance(session.AccountId));
        Assert.Equal(0, Assert.Single(world.Events.Enqueued.OfType<OnlineTimeRewardGranted>()).Cera);
    }

    [Fact]
    public async Task A_disabled_reward_never_pays()
    {
        var world = new World(OnlineTimeRewardRule.Disabled);
        var session = world.Enter(1, "c1");
        await world.AccrueAsync(session);
        world.Clock.Advance(TimeSpan.FromHours(1));

        Assert.False(await world.AccrueAsync(session));
        Assert.Empty(world.Events.Enqueued);
    }

    [Fact]
    public async Task Paying_outside_a_transaction_is_a_programming_error()
    {
        var world = new World(new(true, TimeSpan.FromMinutes(1), 1, 0, false), transaction: false);
        var session = world.Enter(1, "c1");
        await world.AccrueAsync(session);
        world.Clock.Advance(TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.AccrueAsync(session));
    }

    [Fact]
    public void Settings_are_read_and_validated()
    {
        Assert.Same(OnlineTimeRewardRule.Disabled, new OnlineTimeRewardRuleService(new Settings(false, 0, 0, 0)).Rule);
        Assert.Equal(new OnlineTimeRewardRule(true, TimeSpan.FromMinutes(10), 3, 0, true),
            new OnlineTimeRewardRuleService(new Settings(true, 10, 3, 0)).Rule);
        Assert.Throws<InvalidOperationException>(() => new OnlineTimeRewardRuleService(new Settings(true, 0, 3, 0)));
        Assert.Throws<InvalidOperationException>(() => new OnlineTimeRewardRuleService(new Settings(true, 1441, 3, 0)));
        Assert.Throws<InvalidOperationException>(() => new OnlineTimeRewardRuleService(new Settings(true, 5, -1, 3)));
        Assert.Throws<InvalidOperationException>(() => new OnlineTimeRewardRuleService(new Settings(true, 5, 0, 0)));
    }

    private sealed class World
    {
        private readonly OnlineStateRegistry state;

        public World(OnlineTimeRewardRule rule, bool transaction = true)
        {
            Sessions = TestSessions.Directory(out state);
            Rewards = new OnlineTimeRewardDomainService(new Rules(rule), Sessions, Wallets, Events,
                new Transactions(transaction ? new Transaction() : null), Clock);
        }

        public SelectedSessionDomainService Sessions { get; }
        public Wallets Wallets { get; } = new();
        public RecordingEvents Events { get; } = new();
        public Clock Clock { get; } = new();
        public OnlineTimeRewardDomainService Rewards { get; }

        public SelectedSessionIdentity Enter(int account, string connection) =>
            Sessions.InWorld(state, new AccountId(account), new CharacterId(account * 100), connection);

        public void Leave(SelectedSessionIdentity session) => TestGameplay.Run(() => Sessions.ReleaseExact(session));

        public Task<bool> AccrueAsync(SelectedSessionIdentity session) => TestGameplay.RunAsync(() => Rewards.AccrueAsync(session, default).AsTask());

        public void Retain(params SelectedSessionIdentity[] inWorld) => TestGameplay.Run(() => Rewards.Retain(inWorld));
    }

    private sealed class Rules(OnlineTimeRewardRule rule) : IOnlineTimeRewardRuleService
    {
        public OnlineTimeRewardRule Rule => rule;
    }

    private sealed class Settings(bool enabled, int minutes, int cera, int token) : IServerConfigService
    {
        public T GetConfig<T>(string key) => key switch
        {
            "OnlineTimeReward:Enabled" => (T)(object)enabled,
            "OnlineTimeReward:IntervalMinutes" => (T)(object)minutes,
            "OnlineTimeReward:Cera" => (T)(object)cera,
            "OnlineTimeReward:TokenCera" => (T)(object)token,
            "OnlineTimeReward:Notice" => (T)(object)true,
            _ => throw new InvalidOperationException(key),
        };
    }

    private sealed class Wallets : IAccountWalletDomainService
    {
        private readonly Dictionary<AccountId, AccountCeraBalances> balances = [];
        private readonly AccountWalletRuleService rules = new();

        public AccountCeraBalances Balance(AccountId account) => balances.GetValueOrDefault(account, new(0, 0, 0));
        public void Set(AccountId account, AccountCeraBalances value) => balances[account] = value;
        public ValueTask<AccountCeraBalances?> ReadAsync(AccountId account, CancellationToken ct) => ValueTask.FromResult<AccountCeraBalances?>(Balance(account));
        public ValueTask<WalletChange> AddAsync(AccountId account, AccountCurrency currency, int amount, CancellationToken ct)
        {
            var before = Balance(account);
            var after = rules.Add(before, currency, amount);
            balances[account] = after;
            return ValueTask.FromResult(new WalletChange(before, after));
        }
        public ValueTask<WalletChange> SpendAsync(AccountId account, AccountCurrency currency, int amount, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> TryApplyAsync(AccountId account, AccountCeraBalances expected, AccountCeraBalances next, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingEvents : IEventService
    {
        public List<object> Enqueued { get; } = [];
        public ValueTask<Guid> EnqueueAsync<T>(T payload, CancellationToken ct = default) where T : class, IDomainEvent
        {
            Enqueued.Add(payload);
            return ValueTask.FromResult(Guid.NewGuid());
        }
        public ValueTask DirectAsync<T>(T payload, CancellationToken ct = default) where T : class, IDomainEvent => throw new NotSupportedException();
        public ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience, T notice, CancellationToken ct = default)
            where T : class, INotification<T> => throw new NotSupportedException();
    }

    private sealed class Transaction : IDomainTransaction
    {
        public TParticipant Enlist<TParticipant>(object identity, Func<TParticipant> create) where TParticipant : ITransactionParticipant =>
            throw new NotSupportedException();
    }

    private sealed class Transactions(IDomainTransaction? current) : IDomainTransactionAccessor
    {
        public IDomainTransaction? Current => current;
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }
}
