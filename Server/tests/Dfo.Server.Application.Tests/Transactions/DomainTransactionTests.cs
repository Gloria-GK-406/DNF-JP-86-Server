using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;

namespace Dfo.Server.Application.Tests.Transactions;

/// <summary>
/// The guarantee under test is the ordering, not the participants: everything that can fail
/// happens before anything becomes visible. Test participants stand in for the real ones so the
/// guarantee is proved without a database.
/// </summary>
public sealed class DomainTransactionTests
{
    [Fact]
    public async Task PrepareFailureWithRollbackFailureIsUncertainAndKeepsBothCauses()
    {
        var transaction = new DomainTransaction();
        var original = new IOException("prepare failed");
        var rollback = new IOException("rollback failed");
        var participant = Enlist(transaction, [], "durable", true);
        participant.PrepareFailure = original;
        participant.RollbackFailure = rollback;
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(async () =>
            await transaction.CommitAsync(default));
        Assert.Equal(TransactionUncertaintyReason.RollbackFailure, error.Reason);
        var aggregate = Assert.IsType<AggregateException>(error.InnerException);
        Assert.Contains(original, aggregate.InnerExceptions);
        Assert.Contains(rollback, aggregate.InnerExceptions);
        // Disposal/Abort cannot replace the already reported uncertainty or throw it again.
        await transaction.RollbackAsync();
    }

    [Fact]
    public void ClassifierWalksAllAggregateBranchesAndWrappersWithoutGuessingOrdinaryIoFailures()
    {
        var ordinary = new IOException("same words: uncertain commit");
        var typed = new TransactionOutcomeUncertainException(TransactionUncertaintyReason.RollbackFailure,
            "known uncertainty", ordinary);
        var wrapped = new AggregateException(new InvalidOperationException("unrelated"),
            new IOException("quest wrapper", new AggregateException(ordinary, typed)));
        Assert.Same(typed, TransactionOutcomeUncertainException.Find(wrapped));
        Assert.False(TransactionOutcomeUncertainException.Contains(ordinary));
    }

    [Fact]
    public async Task NonFallibleViolation_IsTypedAndPreservesTheOriginalCause()
    {
        var transaction = new DomainTransaction();
        var cause = new IOException("publication violated its contract");
        Enlist(transaction, [], "memory", false).CommitFailure = cause;
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(async () =>
            await transaction.CommitAsync(CancellationToken.None));
        Assert.Equal(TransactionUncertaintyReason.NonFallibleCommitContractViolation, error.Reason);
        Assert.Same(cause, error.InnerException);
    }

    [Fact]
    public async Task Commit_MakesEveryParticipantsChangeVisible()
    {
        var log = new List<string>();
        var transaction = new DomainTransaction();
        var durable = Enlist(transaction, log, "durable", commitCanFail: true);
        var volatileOne = Enlist(transaction, log, "memory", commitCanFail: false);

        await transaction.CommitAsync(CancellationToken.None);

        Assert.True(durable.Applied);
        Assert.True(volatileOne.Applied);
    }

    [Fact]
    public async Task Commit_CommitsTheFallibleParticipantBeforeAnyOther()
    {
        // Enlisted the other way round on purpose: the fallible participant goes first because
        // it is the one that can still fail, not because it happened to join first.
        var log = new List<string>();
        var transaction = new DomainTransaction();
        Enlist(transaction, log, "memory", commitCanFail: false);
        Enlist(transaction, log, "durable", commitCanFail: true);

        await transaction.CommitAsync(CancellationToken.None);

        Assert.Equal(
            new[] { "prepare:memory", "prepare:durable", "commit:durable", "commit:memory" },
            log);
    }

    [Fact]
    public async Task PrepareFailure_LeavesNoParticipantsChangeVisible()
    {
        var log = new List<string>();
        var transaction = new DomainTransaction();
        var durable = Enlist(transaction, log, "durable", commitCanFail: true);
        var volatileOne = Enlist(transaction, log, "memory", commitCanFail: false);
        volatileOne.PrepareFailure = new InvalidOperationException("no room");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transaction.CommitAsync(CancellationToken.None));

        Assert.Equal("no room", failure.Message);
        Assert.False(durable.Applied);
        Assert.False(volatileOne.Applied);
        Assert.True(durable.RolledBack);
        Assert.True(volatileOne.RolledBack);
        Assert.DoesNotContain(log, entry => entry.StartsWith("commit:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DurableCommitFailure_LeavesNoParticipantsChangeVisible()
    {
        var log = new List<string>();
        var transaction = new DomainTransaction();
        var durable = Enlist(transaction, log, "durable", commitCanFail: true);
        var volatileOne = Enlist(transaction, log, "memory", commitCanFail: false);
        durable.CommitFailure = new InvalidOperationException("database is locked");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transaction.CommitAsync(CancellationToken.None));

        Assert.Equal("database is locked", failure.Message);
        Assert.False(durable.Applied);
        Assert.False(volatileOne.Applied);
        Assert.True(volatileOne.RolledBack);
    }

    [Fact]
    public void Enlist_RejectsASecondParticipantWhoseCommitCanFail()
    {
        var log = new List<string>();
        var transaction = new DomainTransaction();
        var first = Enlist(transaction, log, "durable", commitCanFail: true);

        var failure = Assert.Throws<InvalidOperationException>(() =>
            Enlist(transaction, log, "second-durable", commitCanFail: true));

        Assert.Contains("at most one", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(first.Applied);
        Assert.False(first.RolledBack);
        Assert.Empty(log);
    }

    [Fact]
    public void Enlist_AdmitsAnyNumberOfParticipantsWhoseCommitCannotFail()
    {
        var log = new List<string>();
        var transaction = new DomainTransaction();
        Enlist(transaction, log, "memory-a", commitCanFail: false);
        Enlist(transaction, log, "memory-b", commitCanFail: false);
        Enlist(transaction, log, "memory-c", commitCanFail: false);

        Assert.Empty(log);
    }

    [Fact]
    public async Task Enlist_YieldsTheSameParticipantForTheSameIdentity()
    {
        var log = new List<string>();
        var transaction = new DomainTransaction();
        var created = 0;

        RecordingParticipant Create()
        {
            created++;
            return new RecordingParticipant("store", log, commitCanFail: true);
        }

        var first = transaction.Enlist("store", Create);
        var second = transaction.Enlist("store", Create);

        Assert.Same(first, second);
        Assert.Equal(1, created);

        await transaction.CommitAsync(CancellationToken.None);
        Assert.Equal(new[] { "prepare:store", "commit:store" }, log);
    }

    [Fact]
    public async Task Rollback_DiscardsEveryParticipantsChange()
    {
        var log = new List<string>();
        var transaction = new DomainTransaction();
        var durable = Enlist(transaction, log, "durable", commitCanFail: true);
        var volatileOne = Enlist(transaction, log, "memory", commitCanFail: false);

        await transaction.RollbackAsync();

        Assert.True(durable.RolledBack);
        Assert.True(volatileOne.RolledBack);
        Assert.DoesNotContain(log, entry => entry.StartsWith("commit:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rollback_only_commit_discards_changes_and_after_commit_effects()
    {
        var log = new List<string>(); var tx = new DomainTransaction();
        var durable = Enlist(tx, log, "durable", true); var memory = Enlist(tx, log, "memory", false);
        var published = false;
        tx.RegisterAfterCommit("effect", _ => { published = true; return ValueTask.CompletedTask; });
        tx.RequireRollback(); await tx.CommitAsync(default);
        Assert.True(durable.RolledBack); Assert.True(memory.RolledBack);
        Assert.False(durable.Applied); Assert.False(memory.Applied); Assert.False(published);
        Assert.DoesNotContain(log, x => x.StartsWith("prepare:", StringComparison.Ordinal));
    }

    private static RecordingParticipant Enlist(
        DomainTransaction transaction,
        List<string> log,
        string name,
        bool commitCanFail) =>
        transaction.Enlist(name, () => new RecordingParticipant(name, log, commitCanFail));
}
