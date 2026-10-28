using Dfo.Server.Abstractions.Execution;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

/// <summary>One immutable record per character being played, replaced whole on every change.</summary>
/// <remarks>
/// Vitals, cooldowns and the spirit potion are compare-and-swap on the record, so two commands of
/// one account running side by side cannot lose each other's change, and none takes a
/// mutual-exclusion primitive. Only <see cref="Begin"/> inserts; every other write replaces a record
/// its session still owns, so a write that arrives after <see cref="EndExact"/> finds nothing to
/// replace and cannot bring the record back.
/// <para>
/// The position is not on the record: it is the location service's, written only from here, because
/// a character's place in its town area is the same fact and the area needs it under its own lock to
/// show everyone standing there.
/// </para>
/// </remarks>
internal sealed class OnlineCharacterRuntimeDomainService(
    TimeProvider clock, OnlineCharacterLocationDomainService locations)
    : IOnlineCharacterRuntimeDomainService
{
    private readonly ConcurrentDictionary<CharacterId, Entry> entries = new();

    public TownAreaMembershipChange Begin(
        SelectedSessionIdentity session,
        TownAreaKey area,
        CharacterPositionState position,
        bool sharesTownPresence,
        byte hpPercent)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterRuntimeDomainService) + "." + nameof(Begin));
        ArgumentNullException.ThrowIfNull(session);
        var standing = locations.Begin(session, area, position, sharesTownPresence);
        entries[session.CharacterId] = new Entry(
            session, Math.Min(hpPercent, (byte)100),
            ImmutableDictionary<ItemCooldownKey, long>.Empty, Potion: null);
        return standing;
    }

    public bool EndExact(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterRuntimeDomainService) + "." + nameof(EndExact));
        ArgumentNullException.ThrowIfNull(session);
        var ended = locations.End(session);
        while (entries.TryGetValue(session.CharacterId, out var current) && current.Session == session)
        {
            if (entries.TryRemove(KeyValuePair.Create(session.CharacterId, current)))
                return true;
        }
        return ended;
    }

    public CharacterPositionState? PositionOf(SelectedSessionIdentity session) => locations.PositionOf(session);

    public CharacterPositionState? PositionOf(CharacterId characterId) => locations.PositionOf(characterId);

    public TownAreaMember? StandingOf(SelectedSessionIdentity session) => locations.StandingOf(session);

    public TownAreaMembershipChange? CrossInto(
        SelectedSessionIdentity session,
        TownAreaKey destination,
        short x,
        short y,
        bool sharesTownPresence,
        byte? direction = null)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterRuntimeDomainService) + "." + nameof(CrossInto));
        if (locations.StandingOf(session) is not { } standing)
            return null;
        var position = standing.Position with
        {
            TownId = destination.TownId,
            AreaId = destination.AreaId,
            X = x,
            Y = y,
            Direction = direction ?? standing.Position.Direction,
        };
        return locations.Transfer(session, destination, position, sharesTownPresence);
    }

    public TownAreaMovement? StepTo(SelectedSessionIdentity session, short x, short y, byte direction)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterRuntimeDomainService) + "." + nameof(StepTo));
        return locations.StandingOf(session) is { } standing
            ? locations.Step(session, standing.Position with { X = x, Y = y, Direction = direction })
            : null;
    }

    public TownAreaMembershipChange? LeaveTown(SelectedSessionIdentity session, TownAreaDepartureReason reason)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterRuntimeDomainService) + "." + nameof(LeaveTown));
        return locations.Depart(session, reason);
    }

    public TownAreaMembershipChange? ReturnToTown(
        SelectedSessionIdentity session,
        TownAreaKey area,
        CharacterPositionState position,
        bool sharesTownPresence)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterRuntimeDomainService) + "." + nameof(ReturnToTown));
        return locations.Arrive(session, area, position, sharesTownPresence);
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
        byte HpPercent,
        ImmutableDictionary<ItemCooldownKey, long> Cooldowns,
        SpiritPotion? Potion)
    {
        public bool Equals(Entry? other) => ReferenceEquals(this, other);

        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
}
