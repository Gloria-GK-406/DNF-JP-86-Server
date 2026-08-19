using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Application.Tests.Transactions;

/// <summary>
/// Stands in for a real participant. It records the stage it was asked to run and only becomes
/// <see cref="Applied"/> on commit, so a test can tell "prepared" from "visible".
/// </summary>
internal sealed class RecordingParticipant(string name, List<string> log, bool commitCanFail)
    : ITransactionParticipant
{
    public bool CommitCanFail { get; } = commitCanFail;

    public bool Applied { get; private set; }

    public bool RolledBack { get; private set; }

    public Exception? PrepareFailure { get; set; }

    public Exception? CommitFailure { get; set; }

    public ValueTask PrepareAsync(CancellationToken cancellationToken)
    {
        if (this.PrepareFailure is not null)
        {
            throw this.PrepareFailure;
        }

        log.Add($"prepare:{name}");
        return ValueTask.CompletedTask;
    }

    public ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        if (this.CommitFailure is not null)
        {
            throw this.CommitFailure;
        }

        log.Add($"commit:{name}");
        this.Applied = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask RollbackAsync(CancellationToken cancellationToken)
    {
        log.Add($"rollback:{name}");
        this.Applied = false;
        this.RolledBack = true;
        return ValueTask.CompletedTask;
    }
}
