using Dfo.Server.Abstractions.Execution;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

/// <summary>One immutable record per character being played, replaced whole on every change.</summary>
/// <remarks>
/// Changes are compare-and-swap on the record, so two commands of one account running side by side
/// cannot lose each other's change, and none takes a mutual-exclusion primitive. Only
/// <see cref="Begin"/> inserts; every other write replaces a record its session still owns, so a
/// write that arrives after <see cref="EndExact"/> finds nothing to replace and cannot bring the
/// record back.
/// </remarks>
internal sealed class OnlineCharacterRuntimeDomainService(TimeProvider clock)
    : IOnlineCharacterRuntimeDomainService
{
    private readonly ConcurrentDictionary<CharacterId, Entry> entries = new();

    public void Begin(SelectedSessionIdentity session, CharacterPositionState position, byte hpPercent)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterRuntimeDomainService) + "." + nameof(Begin));
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(position);
        entries[session.CharacterId] = new Entry(
            session, position, Math.Min(hpPercent, (byte)100),
            ImmutableDictionary<ItemCooldownKey, long>.Empty, Potion: null);
    }

    public bool EndExact(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterRuntimeDomainService) + "." + nameof(EndExact));
        ArgumentNullException.ThrowIfNull(session);
        while (entries.TryGetValue(session.CharacterId, out var current) && current.Session == session)
        {
            if (entries.TryRemove(KeyValuePair.Create(session.CharacterId, current)))
                return true;
        }
        return false;
    }

    public CharacterPositionState? PositionOf(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return entries.TryGetValue(session.CharacterId, out var entry) && entry.Session == session
            ? entry.Position
            : null;
    }

    public CharacterPositionState? PositionOf(CharacterId characterId) =>
        entries.TryGetValue(characterId, out var entry) ? entry.Position : null;

    public CharacterPositionState CrossInto(
        SelectedSessionIdentity session, byte townId, byte areaId, short x, short y)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterRuntimeDomainService) + "." + nameof(CrossInto));
        return Move(session, current => current with { TownId = townId, AreaId = areaId, X = x, Y = y });
    }

    public CharacterPositionState StepTo(
        SelectedSessionIdentity session, short x, short y, byte direction)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterRuntimeDomainService) + "." + nameof(StepTo));
        return Move(session, current => current with { X = x, Y = y, Direction = direction });
    }

    public byte HpPercentOf(CharacterId characterId) =>
        entries.TryGetValue(characterId, out var entry) ? entry.HpPercent : (byte)0;

    public bool IsCoolingDown(CharacterId characterId, ItemCooldownKey key) =>
        entries.TryGetValue(characterId, out var entry)
        && entry.Cooldowns.TryGetValue(key, out var expiresAt)
        && clock.GetUtcNow().UtcTicks < expiresAt;

    /// <remarks>
    /// Expired cooldowns are left in place until the same key is used again or the session ends:
    /// nothing happens when a cooldown runs out except that the next question is answered
    /// differently, and the record is bounded by the cooldown groups one session can touch.
    /// </remarks>
    public void StartCooldown(SelectedSessionIdentity session, ItemCooldownKey key, TimeSpan duration)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterRuntimeDomainService) + "." + nameof(StartCooldown));
        ArgumentNullException.ThrowIfNull(session);
        if (duration <= TimeSpan.Zero)
            return;
        var expiresAt = clock.GetUtcNow().UtcTicks + duration.Ticks;
        _ = TryChange(session, current => current with { Cooldowns = current.Cooldowns.SetItem(key, expiresAt) });
    }

    public void ApplySpiritPotion(SelectedSessionIdentity session, double bonusRate, TimeSpan duration)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterRuntimeDomainService) + "." + nameof(ApplySpiritPotion));
        ArgumentNullException.ThrowIfNull(session);
        if (!double.IsFinite(bonusRate) || bonusRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(bonusRate));
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
        var potion = new SpiritPotion(bonusRate, clock.GetUtcNow().Add(duration));
        _ = TryChange(session, current => current with { Potion = potion });
    }

    public double SpiritPotionBonusRateOf(CharacterId characterId) =>
        entries.TryGetValue(characterId, out var entry)
        && entry.Potion is { } potion
        && clock.GetUtcNow() < potion.ExpiresAt
            ? potion.Rate
            : 0;

    private CharacterPositionState Move(
        SelectedSessionIdentity session, Func<CharacterPositionState, CharacterPositionState> report)
    {
        ArgumentNullException.ThrowIfNull(session);
        return TryChange(session, current => current with { Position = report(current.Position) })?.Position
            ?? throw new InvalidOperationException(
                "The session reporting a movement is not the one standing there.");
    }

    /// <summary>Replaces the record this session owns, or answers null when it owns none.</summary>
    private Entry? TryChange(SelectedSessionIdentity session, Func<Entry, Entry> change)
    {
        while (entries.TryGetValue(session.CharacterId, out var current) && current.Session == session)
        {
            var next = change(current);
            if (entries.TryUpdate(session.CharacterId, next, current))
                return next;
        }
        return null;
    }

    private sealed record SpiritPotion(double Rate, DateTimeOffset ExpiresAt);

    /// <summary>One character's record, compared by reference so a swap only replaces what was read.</summary>
    private sealed record Entry(
        SelectedSessionIdentity Session,
        CharacterPositionState Position,
        byte HpPercent,
        ImmutableDictionary<ItemCooldownKey, long> Cooldowns,
        SpiritPotion? Potion)
    {
        public bool Equals(Entry? other) => ReferenceEquals(this, other);

        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
}
