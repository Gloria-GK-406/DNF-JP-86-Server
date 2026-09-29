using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>The daily allowance, usage and remaining points of one character.</summary>
public readonly record struct CharacterFatigueBalance(ushort Total, ushort Used)
{
    public int ConsumedToday { get; init; }
    public ushort Remaining => Used >= Total ? (ushort)0 : (ushort)(Total - Used);
}

/// <summary>Character fatigue; total is configured base plus the account entitlement bonus.</summary>
/// <remarks>Mutations require a domain transaction. Refund restores balance without reducing cumulative daily spending.</remarks>
public interface ICharacterFatigueDomainService
{
    ValueTask<CharacterFatigueBalance> BalanceOfAsync(CharacterId characterId, CancellationToken cancellationToken);
    ValueTask SpendAsync(CharacterId characterId, ushort points, CancellationToken cancellationToken);
    ValueTask RefundAsync(CharacterId characterId, ushort points, CancellationToken cancellationToken);
}
