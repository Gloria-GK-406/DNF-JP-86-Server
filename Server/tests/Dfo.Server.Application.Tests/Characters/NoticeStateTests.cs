using Dfo.Server.Abstractions.Notices;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Notices;
using Dfo.Server.Data.OnlineUsers;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class NoticeStateTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Seen_records_are_idempotent_isolated_and_survive_only_committed_transactions(bool commit)
    {
        await using var f = new LearningFixture();
        await f.SeedAsync();
        var service = f.Services.GetRequiredService<INoticeStateDomainService>();
        Assert.True(f.Services.GetRequiredService<IOnlineUserQueryDomainService>()
            .TryGetByConnection(f.Session.ConnectionId, out var user));
        var account = NoticeOwner.ForAccount(new(1));
        var character = NoticeOwner.ForCharacter(new(71));
        await using (var tx = f.Scope.Begin())
        {
            Assert.True(await service.MarkSeenAsync(user!.SessionIdentity, account, NoticeGroup.Primary, 8, default));
            Assert.True(await service.MarkSeenAsync(user.SessionIdentity, account, NoticeGroup.Primary, 8, default));
            Assert.True(await service.MarkSeenAsync(user.SessionIdentity, character, NoticeGroup.Secondary, 8, default));
            Assert.True(await service.MarkSeenAsync(user.SessionIdentity, character, NoticeGroup.Secondary, 0, default));
            Assert.True(await service.MarkSeenAsync(user.SessionIdentity, character, NoticeGroup.Secondary, 127, default));
            Assert.False(await service.MarkSeenAsync(user.SessionIdentity, NoticeOwner.ForAccount(new(2)), NoticeGroup.Primary, 8, default));
            Assert.False(await service.MarkSeenAsync(user.SessionIdentity, NoticeOwner.ForCharacter(new(72)), NoticeGroup.Secondary, 8, default));
            Assert.False(await service.MarkSeenAsync(new GameSessionIdentity(OnlineUserId.New(), f.Session.ConnectionId), account, NoticeGroup.Primary, 9, default));
            if (commit) await tx.CommitAsync(default);
        }
        // A fresh provider loads from SQLite, not from the first service's memory.
        await using var restarted = new LearningFixture(f.Path);
        var reloaded = restarted.Services.GetRequiredService<INoticeStateDomainService>();
        Assert.Equal(commit ? new byte[] { 8 } : [], await reloaded.GetSeenAsync(account, NoticeGroup.Primary, default));
        Assert.Equal(commit ? new byte[] { 0,8,127 } : [], await reloaded.GetSeenAsync(character, NoticeGroup.Secondary, default));
        Assert.Empty(await reloaded.GetSeenAsync(account, NoticeGroup.Secondary, default));
        Assert.Empty(await reloaded.GetSeenAsync(NoticeOwner.ForAccount(new(2)), NoticeGroup.Primary, default));
        Assert.Empty(await reloaded.GetSeenAsync(NoticeOwner.ForCharacter(new(72)), NoticeGroup.Secondary, default));
    }

    [Fact]
    public async Task Invalid_records_and_writes_without_transaction_are_rejected()
    {
        await using var f = new LearningFixture();
        await f.SeedAsync();
        var service = f.Services.GetRequiredService<INoticeStateDomainService>();
        var owner = NoticeOwner.ForAccount(new(1));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.MarkSeenAsync(default, owner, NoticeGroup.Primary, 8, default));
        await using var tx = f.Scope.Begin();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.MarkSeenAsync(default, owner, NoticeGroup.Primary, 128, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.GetSeenAsync(owner, (NoticeGroup)99, default));
    }
}
