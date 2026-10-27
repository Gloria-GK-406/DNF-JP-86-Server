using Dfo.Server.Testing;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// TASK-18 section 5.2: the packaged Host fixtures pass every output line to the recorder, which must recognise
/// an assertion exception as the console logger prints it (full type name, then the message).
/// </summary>
public sealed class HostOutputViolationTests
{
    [Theory]
    [InlineData("      Dfo.Server.Abstractions.Execution.GameplayOffExecutorException: Writing PartyService.LeaveParty must happen inside a gameplay job.", "GameplayOffExecutorException")]
    [InlineData(" ---> Dfo.Server.Abstractions.Execution.GameplayLeakedFlowException: bus:Probe was called on a flow leaked from a finished gameplay job", "GameplayLeakedFlowException")]
    [InlineData("      Dfo.Server.Abstractions.Execution.GameplaySelfWaitException: Waiting for the network (send) must not happen on the gameplay executor (root 00000000-0000-0000-0000-000000000000, depth 0: inbound:GameLogin).", "GameplaySelfWaitException")]
    public void An_assertion_exception_in_a_line_of_host_output_is_recognised(string line, string type)
    {
        Assert.Equal(type, GameplayAssertionRecorder.ViolationIn(line));
    }

    [Theory]
    [InlineData("info: Dfo.Server.Host[0] Gameplay timer:CreatureClockService completed")]
    [InlineData("      System.IO.InvalidDataException: Dungeon '7100' maze '0'")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_lines_are_not_violations(string? line)
    {
        Assert.Null(GameplayAssertionRecorder.ViolationIn(line));
    }
}
