using Microsoft.Extensions.DependencyInjection.Extensions;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Tests.Outbox;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// DR-7, VC-6 (TASK-24 ruling 2): the GM Cera reminder answers "attempted" (every reminder registered for
/// sending) or "offline" (some account had no session in the world); a reminder that cannot be composed is an
/// error. Driven through the real event service, notification bus and wallet composer into the outbox.
/// </summary>
public sealed class GmCeraReminderTests
{
    [Fact]
    public async Task An_online_account_is_reminded_and_the_result_is_attempted()
    {
        await using var world = new ReminderWorld();

        var result = await world.Outbound.RunAsync(() => world.RemindAsync([world.Online.AccountId.Value]));
        await world.Outbound.SettledAsync();

        Assert.Equal("attempted", result);
        Assert.Contains(world.Outbound.Messages(world.Online.ConnectionId), message => message is CeraBalanceUpdateMessage);
    }

    [Fact]
    public async Task An_offline_account_in_the_batch_makes_the_result_offline_and_the_online_one_is_still_reminded()
    {
        await using var world = new ReminderWorld();

        var result = await world.Outbound.RunAsync(() => world.RemindAsync([world.Online.AccountId.Value, 8303]));
        await world.Outbound.SettledAsync();

        Assert.Equal("offline", result);
        Assert.Contains(world.Outbound.Messages(world.Online.ConnectionId), message => message is CeraBalanceUpdateMessage);
    }

    /// <summary>
    /// Review finding 2: an account that has a session the reminder cannot reach is "offline" too -- one selected but not
    /// in the world (the bus reports it Gone) and one the wallet composer declines (no wallet to show, Declined).
    /// </summary>
    [Theory]
    [InlineData("gone")]
    [InlineData("declined")]
    public async Task An_account_whose_session_the_reminder_cannot_reach_makes_the_result_offline(string reach)
    {
        await using var world = new ReminderWorld();
        var account = reach == "gone" ? world.SelectedOnly.AccountId : world.Broken.AccountId;
        if (reach == "declined")
            world.Wallets.Missing = account;

        var result = await world.Outbound.RunAsync(() => world.RemindAsync([world.Online.AccountId.Value, account.Value]));
        await world.Outbound.SettledAsync();

        Assert.Equal("offline", result);
        Assert.Contains(world.Outbound.Messages(world.Online.ConnectionId), message => message is CeraBalanceUpdateMessage);
        Assert.Empty(world.Outbound.Messages(reach == "gone" ? world.SelectedOnly.ConnectionId : world.Broken.ConnectionId));
    }

    /// <summary>Plan R-5 as ruled for VC-6 (R7): a composition failure is thrown, after the others were reminded.</summary>
    [Fact]
    public async Task A_reminder_that_cannot_be_composed_is_an_error_after_the_others_were_reminded()
    {
        await using var world = new ReminderWorld();
        world.Wallets.Failing = world.Broken.AccountId;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Outbound.RunAsync(
            () => world.RemindAsync([world.Broken.AccountId.Value, world.Online.AccountId.Value])));
        await world.Outbound.SettledAsync();

        Assert.Contains(world.Broken.AccountId.Value.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Empty(world.Outbound.Messages(world.Broken.ConnectionId));
        Assert.Contains(world.Outbound.Messages(world.Online.ConnectionId), message => message is CeraBalanceUpdateMessage);
    }

    /// <summary>T10: a reminder that fails in transport was still submitted; the sender closes that connection.</summary>
    [Fact]
    public async Task A_reminder_that_fails_in_transport_is_still_attempted_and_its_connection_is_closed()
    {
        await using var world = new ReminderWorld();
        world.Outbound.Transport.Fail(world.Online.ConnectionId);

        var result = await world.Outbound.RunAsync(() => world.RemindAsync([world.Online.AccountId.Value]));
        await world.Outbound.SettledAsync();

        Assert.Equal("attempted", result);
        Assert.True(await world.Outbound.ClosedAsync(world.Online.ConnectionId));
    }

    private sealed class ReminderWorld : IAsyncDisposable
    {
        public ReminderWorld()
        {
            var directory = TestSessions.Directory(out var state);
            Online = directory.InWorld(state, new AccountId(8301), new CharacterId(8301), "cera-online");
            Broken = directory.InWorld(state, new AccountId(8302), new CharacterId(8302), "cera-broken");
            SelectedOnly = directory.Selected(state, new AccountId(8304), new CharacterId(8304), "cera-selected");
            Outbound = new XoDelivery(configure: services =>
            {
                services.AddSingleton<ISelectedSessionDomainService>(directory);
                services.AddSingleton<IAccountWalletDomainService>(Wallets);
                services.AddEventDispatching();
                services.AddNotificationDispatching();
                services.TryAddSingleton(Dfo.Server.Testing.TestRequired.Absent<Dfo.Server.Abstractions.World.ITownAreaDomainService>());
                services.AddNotificationHandler<AccountWalletChanged, AccountWalletChangedHandler>();
                services.AddSingleton<GmCeraReminderDomainService>();
            });
            Outbound.Connect(Online, Broken, SelectedOnly);
        }

        public XoDelivery Outbound { get; }

        public Wallets Wallets { get; } = new();

        public SelectedSessionIdentity Online { get; }

        public SelectedSessionIdentity Broken { get; }

        /// <summary>Selected, not yet in the world.</summary>
        public SelectedSessionIdentity SelectedOnly { get; }

        public async Task<string> RemindAsync(IReadOnlyList<int> accounts) =>
            await Outbound.Services.GetRequiredService<GmCeraReminderDomainService>().RemindAsync(accounts, CancellationToken.None);

        public ValueTask DisposeAsync() => Outbound.DisposeAsync();
    }

    private sealed class Wallets : IAccountWalletDomainService
    {
        public AccountId? Failing { get; set; }

        /// <summary>An account with no wallet row: the composer has nothing to show and declines.</summary>
        public AccountId? Missing { get; set; }

        public ValueTask<AccountCeraBalances?> ReadAsync(AccountId account, CancellationToken ct) =>
            account == Failing
                ? throw new InvalidOperationException("Injected wallet read failure.")
                : ValueTask.FromResult<AccountCeraBalances?>(account == Missing ? null : new AccountCeraBalances(60, 0, 0));

        public ValueTask<WalletChange> AddAsync(AccountId account, AccountCurrency currency, int amount, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<WalletChange> SpendAsync(AccountId account, AccountCurrency currency, int amount, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> TryApplyAsync(AccountId account, AccountCeraBalances expected, AccountCeraBalances next, CancellationToken ct) => throw new NotSupportedException();
    }
}
