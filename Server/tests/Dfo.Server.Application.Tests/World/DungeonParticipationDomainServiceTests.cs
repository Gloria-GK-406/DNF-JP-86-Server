using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonParticipationDomainServiceTests
{
    [Fact]
    public async Task Prepared_claim_excludes_an_interleaved_transaction_before_commit_or_rollback()
    {
        var accessor = new Accessor(); var service = new DungeonParticipationDomainService(accessor);
        var boss = new DungeonParticipationOwner(DungeonParticipationMode.BossTower, Guid.NewGuid());
        var ordinary = new DungeonParticipationOwner(DungeonParticipationMode.Ordinary, Guid.NewGuid());
        var first = new DomainTransaction(); accessor.Current = first;
        Assert.True(service.TryClaim([new(1)], boss));
        var barrier = first.Enlist(new object(), () => new Barrier());
        var firstCommit = first.CommitAsync(default).AsTask();
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = new DomainTransaction(); accessor.Current = second;
        Assert.True(service.TryClaim([new(1)], ordinary));
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.CommitAsync(default).AsTask());
        barrier.Release.SetResult(); await firstCommit;
        accessor.Current = null; Assert.Equal(boss, service.Find(new(1)));
        var release = new DomainTransaction(); accessor.Current = release;
        service.Release(new(1), boss); await release.RollbackAsync();
        accessor.Current = null; Assert.Equal(boss, service.Find(new(1)));
        service.ReleaseCommitted(new(1), ordinary); Assert.Equal(boss, service.Find(new(1)));
        service.ReleaseCommitted(new(1), boss); Assert.Null(service.Find(new(1)));
    }
    [Fact]
    public async Task An_aborted_prepared_claim_releases_its_reservation()
    {
        var accessor = new Accessor(); var service = new DungeonParticipationDomainService(accessor);
        var owner = new DungeonParticipationOwner(DungeonParticipationMode.BossTower, Guid.NewGuid());
        var first = new DomainTransaction(); accessor.Current = first; Assert.True(service.TryClaim([new(1)], owner));
        first.Enlist(new object(), () => new FailedPrepare());
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.CommitAsync(default).AsTask());
        var second = new DomainTransaction(); accessor.Current = second; Assert.True(service.TryClaim([new(1)], owner));
        await second.CommitAsync(default); accessor.Current = null; Assert.Equal(owner, service.Find(new(1)));
    }
    private sealed class Accessor : IDomainTransactionAccessor { public IDomainTransaction? Current { get; set; } }
    private sealed class Barrier : ITransactionParticipant
    {
        public bool CommitCanFail => true;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public async ValueTask CommitAsync(CancellationToken ct) { Entered.SetResult(); await Release.Task; }
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
    private sealed class FailedPrepare : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => throw new InvalidOperationException("fail after claim preparation");
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
}
