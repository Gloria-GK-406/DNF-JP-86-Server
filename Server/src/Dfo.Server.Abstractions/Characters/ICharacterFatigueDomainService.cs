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
    async ValueTask<bool> CanEnterDungeonAsync(CharacterId characterId, CancellationToken token) =>
        (await BalanceOfAsync(characterId, token).ConfigureAwait(false)).Remaining > 0;
    ValueTask<CharacterFatigueBalance> BalanceAtAsync(CharacterId characterId, DateTimeOffset at, CancellationToken token) =>
        ValueTask.FromException<CharacterFatigueBalance>(new NotSupportedException("Explicit-time fatigue read is unavailable."));
    /// <summary>
    /// Fatigue spent on the game day <paramref name="day"/> (named as <c>IDailyResetRuleService.PeriodDay</c> names it);
    /// 0 when the last recorded spending belongs to another day, since only the latest day is kept.
    /// </summary>
    ValueTask<int> ConsumedOnAsync(CharacterId characterId, DateOnly day, CancellationToken token) =>
        ValueTask.FromException<int>(new NotSupportedException("Per-day fatigue read is unavailable."));
    ValueTask<CharacterFatigueBalance> BalanceOfAsync(CharacterId characterId, CancellationToken cancellationToken);
    ValueTask SpendAsync(CharacterId characterId, ushort points, CancellationToken cancellationToken);
    ValueTask RefundAsync(CharacterId characterId, ushort points, CancellationToken cancellationToken);
}
