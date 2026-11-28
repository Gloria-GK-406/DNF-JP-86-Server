using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Benefits;

namespace Dfo.Server.Application.World;

internal readonly record struct DungeonFatigueContext(DungeonRunParticipant Participant, DungeonLayout Layout, bool IsEntry);

internal sealed class DungeonFatigueBurningBenefits(LuckyCharacterBenefitRuleService rules)
    : IGameplayBenefitSource<DungeonFatigueContext>
{
    public ValueTask<GameplayBenefitBatch> DescribeAsync(DungeonFatigueContext context, DateTimeOffset at, CancellationToken ct) =>
        ValueTask.FromResult(new GameplayBenefitBatch(LuckyCharacterBenefitRuleService.RunSource,
            GameplayBenefitScope.For(context.Participant.CharacterId),
            context.Participant.BurnsFatigue ? [rules.Burning] : []));
}

internal readonly record struct DungeonFatiguePayment(CharacterFatigueCharge Charge, bool BurnsFatigue = false);

internal sealed class DungeonFatigueCosts(
    ICharacterFatigueDomainService fatigue, IFatigueConsumptionQuery consumption, GameplayBenefitsQuery<DungeonFatigueContext> benefits)
{
    public async ValueTask<IReadOnlyList<DungeonFatiguePayment>> PrepareAsync(
        IReadOnlyList<DungeonRunParticipant> participants, DungeonLayout layout, bool isEntry, DateTimeOffset at, CancellationToken ct)
    {
        if (!layout.ConsumesFatigue || (!isEntry && layout.EntryOnlyFatigue is not null)) return [];
        var baseCost = layout.EntryOnlyFatigue ?? 1;
        var payments = new List<DungeonFatiguePayment>(participants.Count);
        bool? shouldConsume = null;
        foreach (var participant in participants)
        {
            var character = participant.CharacterId;
            var snapshot = await benefits.ReadAtAsync(new(participant, layout, isEntry), at, ct, GameplayBenefitScope.Global,
                GameplayBenefitScope.For(participant.AccountId), GameplayBenefitScope.For(character)).ConfigureAwait(false);
            decimal regular = baseCost;
            decimal fullChargeFactor = 1;
            var requiresFullCharge = false;
            foreach (var effect in snapshot.Effects<FatigueCostMultiplier>())
            {
                if (effect.Factor <= 0)
                    throw new InvalidOperationException("Fatigue cost multipliers must be positive.");
                if (effect.RequireFullCharge)
                {
                    fullChargeFactor *= effect.Factor;
                    requiresFullCharge = true;
                }
                else
                    regular *= effect.Factor;
            }

            var fullCost = decimal.Ceiling(regular * fullChargeFactor);
            if (requiresFullCharge && fullCost is > 0 and <= ushort.MaxValue
                && (shouldConsume ??= await consumption.ShouldConsumeAsync(ct).ConfigureAwait(false))
                && (await fatigue.BalanceAtAsync(character, at, ct).ConfigureAwait(false)).Remaining >= fullCost)
            {
                var includesBurning = snapshot.Active.Any(entry =>
                    entry.Source == LuckyCharacterBenefitRuleService.RunSource
                    && entry.Benefit.Key == LuckyCharacterBenefitRuleService.BurningKey);
                payments.Add(new(new(character, (ushort)fullCost, RequireFullCharge: true), includesBurning));
            }
            else
                payments.Add(new(new(character, checked((ushort)decimal.Ceiling(regular)))));
        }
        return payments;
    }
}
