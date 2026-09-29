using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Support;

// Runtime-registry wiring tests intentionally omit persistence and entitlement modules.
internal sealed class FixedFatigue : ICharacterFatigueDomainService
{
    public ValueTask<CharacterFatigueBalance> BalanceOfAsync(CharacterId id, CancellationToken token) =>
        ValueTask.FromResult(new CharacterFatigueBalance(188, 0));
    public ValueTask SpendAsync(CharacterId id, ushort points, CancellationToken token) => ValueTask.CompletedTask;
    public ValueTask RefundAsync(CharacterId id, ushort points, CancellationToken token) => ValueTask.CompletedTask;
}
