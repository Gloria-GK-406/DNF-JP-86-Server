using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.World;

internal interface ILuckyDungeonEntryBenefits
{
    ValueTask<(IReadOnlyList<DungeonRunParticipant> Participants, LuckyCharacterPeriod Period)> CaptureAsync(
        IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout, DateTimeOffset now, CancellationToken ct);
    ValueTask<ImmutableHashSet<CharacterId>> SelectRoomBurningAsync(IReadOnlyList<DungeonRunParticipant> participants,
        DateTimeOffset now, CancellationToken ct);
}

internal sealed class LuckyDungeonEntryBenefits(ILuckyCharacterEventQuery events, ILuckyCharacterBurning burning,
    ICharacterDomainService characters, ICharacterFatigueDomainService fatigue, IFatigueConsumptionQuery consumption,
    IFatigueBurningDungeonContentSource dungeonRestrictions)
    : ILuckyDungeonEntryBenefits
{
    public async ValueTask<(IReadOnlyList<DungeonRunParticipant> Participants, LuckyCharacterPeriod Period)> CaptureAsync(
        IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout, DateTimeOffset now, CancellationToken ct)
    {
        if (layout.Mode.Kind != DungeonRunKind.Ordinary)
            return (participants, LuckyCharacterPeriod.Closed);
        var period = await events.ReadAtAsync(now, ct).ConfigureAwait(false);
        if (period.Jobs.IsEmpty)
            return (participants, period);

        var baseCost = layout.EntryOnlyFatigue ?? 1;
        var canBurn = layout.ConsumesFatigue && baseCost is > 0 and <= ushort.MaxValue / 2
            && await consumption.ShouldConsumeAsync(ct).ConfigureAwait(false);
        var result = new List<DungeonRunParticipant>(participants.Count);
        foreach (var participant in participants)
        {
            var identity = await characters.Get(participant.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
            var lucky = identity is not null && period.Includes(identity.Job);
            var burnsFatigue = lucky && canBurn && burning.IsEnabled(participant.CharacterId, period.Revision)
                && !dungeonRestrictions.LoadProhibitedDungeons().Contains(layout.DungeonId)
                && (await fatigue.BalanceAtAsync(participant.CharacterId, now, ct).ConfigureAwait(false)).Remaining >= 2 * baseCost;
            result.Add(participant with { IsLuckyCharacter = lucky, BurnsFatigue = burnsFatigue });
        }
        return (result, period);
    }

    public async ValueTask<ImmutableHashSet<CharacterId>> SelectRoomBurningAsync(
        IReadOnlyList<DungeonRunParticipant> participants, DateTimeOffset now, CancellationToken ct)
    {
        if (!participants.Any(participant => participant.BurnsFatigue)
            || !await consumption.ShouldConsumeAsync(ct).ConfigureAwait(false))
            return [];

        var paying = ImmutableHashSet.CreateBuilder<CharacterId>();
        foreach (var participant in participants)
        {
            if (participant.BurnsFatigue &&
                (await fatigue.BalanceAtAsync(participant.CharacterId, now, ct).ConfigureAwait(false)).Remaining >= 2)
                paying.Add(participant.CharacterId);
        }
        return paying.ToImmutable();
    }
}
