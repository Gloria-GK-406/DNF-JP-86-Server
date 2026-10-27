using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Transactions;

[Trait("Category", "Unit")]
public sealed class ReadOnlyCommandFailureTests
{
    [Fact]
    public async Task Query_failure_keeps_original_cause_when_snapshot_cleanup_also_fails()
    {
        var probe = new Failures(new IOException("query storage read failed"), new IOException("snapshot cleanup failed"));
        var services = new ServiceCollection().AddLogging().AddCommandDispatching();
        services.AddSingleton(probe);
        services.AddFixtureCommand<QueryCommand, long, QueryHandler>(FixtureUnitKind.ReadOnly);
        await using var provider = services.BuildServiceProvider();

        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(async () =>
            await DomainCommandFixture.Bus(provider).SendAsync(new QueryCommand()));

        Assert.Equal(TransactionUncertaintyReason.RollbackFailure, failure.Reason);
        var causes = Assert.IsType<AggregateException>(failure.InnerException);
        Assert.Contains(probe.ReadFailure, causes.InnerExceptions);
        Assert.Contains(probe.CleanupFailure, causes.InnerExceptions);
        Assert.Null(provider.GetRequiredService<IDomainTransactionAccessor>().Current);
    }

    private sealed record Failures(Exception ReadFailure, Exception CleanupFailure);
    private sealed record QueryCommand : ICommand<long>;
    private sealed class QueryHandler(IDomainTransactionAccessor transactions, Failures probe)
        : ICommandHandler<QueryCommand, long>
    {
        public ValueTask<long> HandleAsync(CommandExecutionContext context, QueryCommand command, CancellationToken ct)
        {
            transactions.Current!.Enlist("snapshot", () => new Snapshot(probe.CleanupFailure));
            throw probe.ReadFailure;
        }
    }
    private sealed class Snapshot(Exception failure) : ITransactionParticipant
    {
        public bool SupportsReadOnly => true;
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => throw failure;
    }
}
