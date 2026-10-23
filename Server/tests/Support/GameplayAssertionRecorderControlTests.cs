using Dfo.Server.Abstractions.Execution;

namespace Dfo.Server.Testing;

/// <summary>
/// The positive control for the zero-violation evidence (TASK-18 section 5.1): linked into every test project
/// that links <see cref="GameplayAssertionRecorder"/>, it makes exactly one off-executor violation, so a run with
/// <c>DFO_GAMEPLAY_ASSERTIONS</c> set must show one record of this class per assembly. A missing record means the
/// recorder is not installed there, and that assembly's zero would prove nothing.
/// </summary>
public sealed class GameplayAssertionRecorderControlTests
{
    [Fact]
    public void An_off_executor_violation_in_this_assembly_reaches_the_recorder()
    {
        var thrown = Assert.Throws<GameplayOffExecutorException>(() => GameplayWriteAssertion.Writing("recorder-control"));

        Assert.Contains("recorder-control", thrown.Message, StringComparison.Ordinal);
    }
}
