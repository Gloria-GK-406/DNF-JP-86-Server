using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Commands;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Transactions;

public sealed partial class ProtocolNonEffectFootprintTests
{
    [Theory]
    [InlineData("allowed")] [InlineData("readonly")] [InlineData("main")] [InlineData("recover")]
    [InlineData("empty")] [InlineData("duplicate")] [InlineData("owner")]
    [InlineData("closed-action")] [InlineData("foreign-context")]
    public async Task Trusted_internal_declared_owners_remain_bound_to_the_actual_ordinary_action(string mode)
    {
        using var world = World(out var state);
        IEffectFootprintRecorder? captured = null;
        var writes = 0;
        state.DeclaredAction = async (context, units, characters) =>
        {
            var declaration = Declared(mode == "empty" ? [] : mode == "duplicate" ? [state.Actor!, state.Actor!] : [state.Actor!]);
            if (mode == "foreign-context") context = context with { Request = null };
            if (mode == "main") declaration = declaration with { Kind = CommandWorkUnitKind.MainSettlement };
            if (mode == "readonly") return await units.ReadOnlyAsync(context, declaration, _ => ValueTask.FromResult(1));
            if (mode == "recover") { await units.WriteResultAsync(context, declaration, (_, _) => ValueTask.FromResult(1)); return 1; }
            var result = await units.WriteAsync(context, declaration, async (unit, token) =>
            {
                captured = unit.Footprint;
                if (mode == "owner")
                    Assert.Throws<InvalidOperationException>(() => captured.DemandEffects(new(state.Actor!.AccountId, new(99), state.Actor)));
                captured.DemandActive(state.Actor!);
                await characters.Get(state.Actor!.CharacterId).TrySpendGoldAsync(1, token);
                writes++;
                return 1;
            });
            if (mode == "closed-action") captured!.DemandActive(state.Actor!);
            return result;
        };
        var error = await Record.ExceptionAsync(() => world.Services.GetRequiredService<ICommandBus>().SendInternalAsync(new InternalCap(Mode.Declared),
            Dfo.Server.Application.Tests.Commands.ProtocolCommandExecutionTests.Origin()).AsTask());
        if (mode == "allowed")
        {
            Assert.Null(error); Assert.Equal(1, writes); Assert.Equal(99999, world.Gold);
            Assert.IsType<CommandRequestContext.TrustedInternal>(state.Context!.Request);
            Assert.Throws<InvalidOperationException>(() => captured!.DemandActive(state.Actor!));
        }
        else
        {
            Assert.NotNull(error);
            Assert.Equal(mode == "closed-action" ? 1 : 0, writes);
            Assert.Equal(mode == "closed-action" ? 99999 : 100000, world.Gold);
        }
        Assert.Empty(world.Sender.Outbound.Closes);
    }
}
