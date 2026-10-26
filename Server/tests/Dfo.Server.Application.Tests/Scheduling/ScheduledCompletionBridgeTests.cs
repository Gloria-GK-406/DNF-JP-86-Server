using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Scheduling;
using Dfo.Server.Testing;
namespace Dfo.Server.Application.Tests.Scheduling;
public sealed class ScheduledCompletionBridgeTests
{
    [Fact]
    public async Task The_completed_report_cannot_be_used_before_close_for_another_command_or_in_another_job()
    {
        ScheduledGameplayTaskResult report = null!;
        ScheduledGameplayTaskCommand command = null!;
        var original = new IOException("already handled business action");
        await TestGameplay.RunAsync(() =>
        {
            command = new("test", new(), 1);
            var request = new CommandRequestContext.TrustedInternal(new(GameplayCallSource.Timer, GameplayJob.Current!.Value, "test:completion-bridge"));
            var context = new CommandExecutionContext(Guid.NewGuid(), typeof(ScheduledGameplayTaskCommand), typeof(ScheduledCompletionBridgeTests), CommandExecutionKind.Direct) { Request = request };
            var business = ScheduledTaskBusinessResult.Handled(context, "original aggregate", [original]);
            report = new(command, context, new ScheduledTaskContext("test", default, default, 1), business);
            Assert.Throws<InvalidOperationException>(() => report.ThrowCompletedBusinessFailure(command));
            request.Close();
            Assert.Throws<InvalidOperationException>(() => report.ThrowCompletedBusinessFailure(command with { }));
            var actual = Assert.Throws<AggregateException>(() => report.ThrowCompletedBusinessFailure(command));
            Assert.Same(original, Assert.Single(actual.InnerExceptions));
            return Task.CompletedTask;
        });
        await TestGameplay.RunAsync(() =>
        {
            Assert.Throws<InvalidOperationException>(() => report.ThrowCompletedBusinessFailure(command));
            return Task.CompletedTask;
        });
    }
}
