using Dfo.Server.Application.Commands;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Application.Tests.Commands.ProtocolCommandExecutionTests;

namespace Dfo.Server.Application.Tests.Commands;

public sealed class CommandRequestContextTests
{
    [Fact]
    public async Task A_missing_request_never_becomes_trusted_internal_work()
    {
        await using var world = new CommandWorld();
        var units = world.Services.GetRequiredService<ICommandWorkUnits>();
        var context = new CommandExecutionContext(Guid.NewGuid(), typeof(InternalProbe), typeof(CommandRequestContextTests), CommandExecutionKind.Direct);
        var ran = false;
        await TestGameplay.RunAsync(world.Services, async () =>
            await Assert.ThrowsAsync<InvalidOperationException>(() => units.WriteAsync(context, new("missing", []),
                (_, _) => { ran = true; return ValueTask.FromResult(1); }).AsTask()));
        Assert.False(ran);
    }

    [Fact]
    public async Task A_closed_request_cannot_open_a_unit_on_a_later_job()
    {
        await using var world = new CommandWorld();
        await world.Internal(new InternalProbe());
        var context = Assert.Single(world.Settings.Seen);
        Assert.True(context.Request!.IsClosed);
        var ran = false;
        await TestGameplay.RunAsync(world.Services, async () =>
            await Assert.ThrowsAsync<InvalidOperationException>(() => world.Services.GetRequiredService<ICommandWorkUnits>()
                .WriteAsync(context, new("closed", []), (_, _) => { ran = true; return ValueTask.FromResult(1); }).AsTask()));
        Assert.False(ran);
    }

    [Fact]
    public async Task Closing_a_request_revokes_it_even_while_its_original_job_is_still_running()
    {
        await using var world = new CommandWorld();
        var ran = false;
        await TestGameplay.RunAsync(world.Services, async () =>
        {
            var context = new CommandExecutionContext(Guid.NewGuid(), typeof(InternalProbe), typeof(CommandRequestContextTests), CommandExecutionKind.Direct)
            { Request = new CommandRequestContext.TrustedInternal(Origin()) };
            context.Request.Close();
            await Assert.ThrowsAsync<InvalidOperationException>(() => world.Services.GetRequiredService<ICommandWorkUnits>()
                .WriteAsync(context, new("closed-same-job", []), (_, _) => { ran = true; return ValueTask.FromResult(1); }).AsTask());
        });
        Assert.False(ran);
    }

    [Fact]
    public async Task Even_an_unclosed_explicit_request_is_owned_by_its_original_job()
    {
        await using var world = new CommandWorld();
        var context = await TestGameplay.RunAsync(world.Services, () => Task.FromResult(
            new CommandExecutionContext(Guid.NewGuid(), typeof(InternalProbe), typeof(CommandRequestContextTests), CommandExecutionKind.Direct)
            { Request = new CommandRequestContext.TrustedInternal(Origin()) }));
        Assert.False(context.Request!.IsClosed);
        var ran = false;
        await TestGameplay.RunAsync(world.Services, async () =>
            await Assert.ThrowsAsync<InvalidOperationException>(() => world.Services.GetRequiredService<ICommandWorkUnits>()
                .WriteAsync(context, new("foreign-job", []), (_, _) => { ran = true; return ValueTask.FromResult(1); }).AsTask()));
        context.Request.Close();
        Assert.False(ran);
    }

    [Fact]
    public async Task An_internal_request_cannot_claim_an_effect_main_before_any_business_runs()
    {
        await using var world = new CommandWorld();
        var ran = false;
        world.Settings.Action = ctx => world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync(ctx,
            new("internal-main", [], CommandWorkUnitKind.MainSettlement), (_, _) => { ran = true; return ValueTask.FromResult(1); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Internal(new InternalProbe()).AsTask());
        Assert.False(ran);
    }
}
