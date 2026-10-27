using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>RQ-050, RQ-052 and the RQ-053 mechanism: chain shape, the named limit, refusal past it.</summary>
public sealed class GameplayCallDepthTests
{
    /// <summary>
    /// Spec appendix A: the longest legal chain on the baseline type graph is 5 + 5 hops
    /// (inbound monster death to achievement notice, then a CMD-registered close to the same tail).
    /// </summary>
    private const int AppendixALongestLegalChain = 10;

    /// <summary>RQ-052: at least twice the longest legal chain and never below 8.</summary>
    private static bool LimitCovers(int limit, int longestLegalChain) =>
        limit >= Math.Max(2 * longestLegalChain, 8);

    [Fact]
    public void The_depth_limit_covers_twice_the_longest_legal_chain_of_appendix_A()
    {
        Assert.True(LimitCovers(GameplayCallDepth.Limit, AppendixALongestLegalChain));
        Assert.Equal(32, GameplayCallDepth.Limit);
    }

    [Fact]
    public void The_depth_criterion_refuses_a_limit_below_twice_the_chain_or_below_eight()
    {
        Assert.False(LimitCovers(2 * AppendixALongestLegalChain - 1, AppendixALongestLegalChain));
        Assert.False(LimitCovers(7, 3));
        Assert.True(LimitCovers(8, 3));
    }

    [Fact]
    public void A_root_starts_at_depth_zero_and_a_child_inherits_root_and_connection()
    {
        var connection = new NetworkConnectionId("conn-9");
        var root = GameplayCallChain.NewRoot(GameplayCallSource.Connection, connection).ForCommand("Login");

        var child = root.Child(GameplayCallSource.Event);
        var grandchild = child.ForCommand("Achievement").Child(GameplayCallSource.Lifecycle);

        Assert.Equal(0, root.Depth);
        Assert.Equal(root.Id, root.RootId);
        Assert.Null(root.ParentId);
        Assert.Equal(1, child.Depth);
        Assert.Equal(root.RootId, child.RootId);
        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal(GameplayCallSource.Event, child.Source);
        Assert.Null(child.CommandType);
        Assert.Equal(connection, grandchild.SourceConnection);
        Assert.Equal(2, grandchild.Depth);
        Assert.Equal(child.Id, grandchild.ParentId);
        Assert.Equal(
            new[]
            {
                new GameplayCallHop(GameplayCallSource.Connection, "Login"),
                new GameplayCallHop(GameplayCallSource.Event, "Achievement"),
            },
            grandchild.Ancestors);
        Assert.Equal("Connection:Login > Event:Achievement > Lifecycle:Close", grandchild.Describe("Close"));
    }

    [Fact]
    public void Every_source_kind_of_the_spec_exists()
    {
        Assert.Equal(
            new[] { "Connection", "Timer", "Event", "Lifecycle", "Gm", "Http", "StartupRecovery" },
            Enum.GetNames<GameplayCallSource>());
    }

    [Fact]
    public void A_long_lineage_is_kept_to_the_most_recent_hops()
    {
        var chain = GameplayCallChain.NewRoot(GameplayCallSource.Timer);
        for (var i = 0; i < GameplayCallDepth.Limit * 3; i++)
            chain = chain.ForCommand($"C{i}").Child(GameplayCallSource.Event);

        Assert.Equal(GameplayCallDepth.Limit * 3, chain.Depth);
        Assert.Equal(GameplayCallDepth.Limit + 1, chain.Ancestors.Count);
        Assert.Equal($"C{GameplayCallDepth.Limit * 3 - 1}", chain.Ancestors[^1].CommandType);
        Assert.StartsWith("... > ", chain.Describe("Last"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Work_at_the_limit_runs_and_one_past_it_is_refused_with_the_full_chain_logged()
    {
        var (executor, log) = ExecutionTest.Executor();
        await using var _executor = executor;

        // Each hop hands its child chain back out, the way an event consumer enqueues the next CMD.
        var chain = ExecutionTest.Root("conn-depth");
        for (var depth = 0; depth <= GameplayCallDepth.Limit; depth++)
        {
            Assert.Equal(depth, chain.Depth);
            var source = depth == 0 ? GameplayCallSource.Connection : GameplayCallSource.Event;
            chain = await executor.RunAsync(ExecutionTest.Work(
                _ => ValueTask.FromResult(GameplayJob.Current!.Value.Child(GameplayCallSource.Event)),
                $"Hop{depth}", chain), CancellationToken.None);
            Assert.Equal(source, chain.Ancestors[^1].Source);
        }

        var ran = false;
        var refused = await Assert.ThrowsAsync<GameplayDepthExceededException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(_ =>
            {
                ran = true;
                return ValueTask.CompletedTask;
            }, "TooDeep", chain), CancellationToken.None));

        Assert.False(ran);
        Assert.Equal(GameplayCallDepth.Limit + 1, refused.Chain.Depth);
        Assert.Equal("TooDeep", refused.CommandType);
        var error = Assert.Single(log.Entries, entry => entry.Level == LogLevel.Error);
        var described = (string)error.Fields["Chain"]!;
        var expected = string.Join(" > ",
            Enumerable.Range(0, GameplayCallDepth.Limit + 1)
                .Select(i => i == 0 ? "Connection:Hop0" : $"Event:Hop{i}")
                .Append("Event:TooDeep"));
        Assert.Equal(expected, described);
        Assert.Equal(chain.RootId, error.Fields["RootId"]);
        Assert.Equal(GameplayCallDepth.Limit + 1, error.Fields["Depth"]);
    }

    [Fact]
    public async Task A_default_chain_is_refused_as_an_argument_error()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask, chain: default(GameplayCallChain)),
                CancellationToken.None));
    }
}
