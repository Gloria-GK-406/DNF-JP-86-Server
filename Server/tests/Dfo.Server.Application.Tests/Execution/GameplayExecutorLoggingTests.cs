using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>RQ-054: start, end and failure logs carry the chain; the end log carries both timings.</summary>
public sealed class GameplayExecutorLoggingTests
{
    [Fact]
    public async Task Start_and_end_logs_carry_root_parent_depth_source_and_type_and_the_end_log_both_timings()
    {
        var (executor, log) = ExecutionTest.Executor();
        await using var _executor = executor;
        var parent = ExecutionTest.Root().ForCommand("Parent");
        var chain = parent.Child(GameplayCallSource.Event);

        await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask, "Logged", chain),
            CancellationToken.None);

        var entries = log.Entries.ToArray();
        var start = Assert.Single(entries, entry => entry.Level == LogLevel.Trace);
        var end = Assert.Single(entries, entry => entry.Level == LogLevel.Debug);
        foreach (var entry in new[] { start, end })
        {
            Assert.Equal(chain.RootId, entry.Fields["RootId"]);
            Assert.Equal(parent.Id, entry.Fields["ParentId"]);
            Assert.Equal(1, entry.Fields["Depth"]);
            Assert.Equal(GameplayCallSource.Event, entry.Fields["Source"]);
            Assert.Equal("Logged", entry.Fields["CommandType"]);
            Assert.IsType<double>(entry.Fields["QueueWaitMilliseconds"]);
        }
        Assert.IsType<double>(end.Fields["ElapsedMilliseconds"]);
        Assert.DoesNotContain(entries, entry => entry.Level >= LogLevel.Information);
    }

    [Fact]
    public async Task The_failure_log_is_an_error_with_the_exception_and_the_chain()
    {
        var (executor, log) = ExecutionTest.Executor();
        await using var _executor = executor;
        var chain = ExecutionTest.Root("conn-log");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(
                _ => throw new InvalidOperationException("broken"), "Broken", chain), CancellationToken.None));

        var failure = Assert.Single(log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.IsType<InvalidOperationException>(failure.Exception);
        Assert.Equal(chain.RootId, failure.Fields["RootId"]);
        Assert.Null(failure.Fields["ParentId"]);
        Assert.Equal(0, failure.Fields["Depth"]);
        Assert.Equal(GameplayCallSource.Connection, failure.Fields["Source"]);
        Assert.Equal("Broken", failure.Fields["CommandType"]);
        Assert.Equal("Connection:Broken", failure.Fields["Chain"]);
        Assert.IsType<double>(failure.Fields["ElapsedMilliseconds"]);
        Assert.DoesNotContain(log.Entries, entry => entry.Level == LogLevel.Debug);
    }

    /// <summary>
    /// F-30 (TASK-31 section 2.3): the producer's own line for a failed job is Debug, so the executor's Error is
    /// the only line at the default level and has to say which connection the job came from.
    /// </summary>
    [Fact]
    public async Task The_failure_log_names_the_source_connection()
    {
        var (executor, log) = ExecutionTest.Executor();
        await using var _executor = executor;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(
                _ => throw new InvalidOperationException("broken"), "Broken", ExecutionTest.Root("conn-source")),
                CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(
                _ => throw new InvalidOperationException("broken"), "NoConnection", ExecutionTest.Root()),
                CancellationToken.None));

        var failures = log.Entries.Where(entry => entry.Level == LogLevel.Error).ToArray();
        Assert.Equal(2, failures.Length);
        Assert.Equal(new Dfo.Server.Data.Networking.NetworkConnectionId("conn-source"),
            Assert.Single(failures, entry => Equals(entry.Fields["CommandType"], "Broken")).Fields["SourceConnection"]);
        Assert.Null(Assert.Single(failures, entry => Equals(entry.Fields["CommandType"], "NoConnection")).Fields["SourceConnection"]);
    }
}
