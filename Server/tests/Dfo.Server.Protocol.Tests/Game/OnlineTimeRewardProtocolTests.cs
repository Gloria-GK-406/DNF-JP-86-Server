using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.OnlineTimeReward;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game;

public sealed class OnlineTimeRewardProtocolTests
{
    [Fact]
    public async Task A_paid_stay_sees_its_new_wallet_then_the_notice()
    {
        var sessions = TestSessions.Directory(out var state);
        var session = sessions.InWorld(state, new AccountId(7), new CharacterId(700), "c7");
        var handler = new OnlineTimeRewardPaidHandler(new Wallet(new(10, 30, 0)), sessions);

        var messages = await handler.ComposeAsync(new NotificationRecipient(session.CharacterId, session.ConnectionId, session),
            new OnlineTimeRewardPaid(session, 10, 30, true), default);

        Assert.Collection(messages,
            message => Assert.Equal(new AccountCeraBalances(10, 30, 0), Assert.IsType<CeraBalanceUpdateMessage>(message).Balances),
            message => Assert.Equal("在线奖励：获得 10 点券、30 代币。", Assert.IsType<GameServerNoticeMessage>(message).Text));
    }

    [Fact]
    public async Task Without_the_notice_only_the_wallet_is_refreshed_and_a_departed_stay_sees_nothing()
    {
        var sessions = TestSessions.Directory(out var state);
        var session = sessions.InWorld(state, new AccountId(7), new CharacterId(700), "c7");
        var handler = new OnlineTimeRewardPaidHandler(new Wallet(new(0, 5, 0)), sessions);
        var recipient = new NotificationRecipient(session.CharacterId, session.ConnectionId, session);

        Assert.IsType<CeraBalanceUpdateMessage>(Assert.Single(
            await handler.ComposeAsync(recipient, new OnlineTimeRewardPaid(session, 0, 5, false), default)));

        TestGameplay.Run(() => sessions.ReleaseExact(session));
        Assert.Empty(await handler.ComposeAsync(recipient, new OnlineTimeRewardPaid(session, 0, 5, true), default));
    }

    [Theory]
    [InlineData(3, 0, "在线奖励：获得 3 点券。")]
    [InlineData(0, 4, "在线奖励：获得 4 代币。")]
    public void The_notice_names_only_what_was_paid(int cera, int token, string expected) =>
        Assert.Equal(expected, OnlineTimeRewardPaidHandler.Text(cera, token));

    private sealed class Wallet(AccountCeraBalances balances) : IAccountWalletDomainService
    {
        public ValueTask<AccountCeraBalances?> ReadAsync(AccountId account, CancellationToken ct) => ValueTask.FromResult<AccountCeraBalances?>(balances);
        public ValueTask<WalletChange> AddAsync(AccountId account, AccountCurrency currency, int amount, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<WalletChange> SpendAsync(AccountId account, AccountCurrency currency, int amount, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> TryApplyAsync(AccountId account, AccountCeraBalances expected, AccountCeraBalances next, CancellationToken ct) => throw new NotSupportedException();
    }
}
