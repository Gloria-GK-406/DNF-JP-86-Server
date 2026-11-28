using System.Collections.Immutable;
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
    ICharacterFatigueDomainService fatigue, IFatigueConsumptionQuery consumption,
    GameplayBenefitsQuery<DungeonFatigueContext> benefits, DungeonFatigueRuleService rules)
{
    public async ValueTask<IReadOnlyList<DungeonFatiguePayment>> PrepareAsync(
        IReadOnlyList<DungeonRunParticipant> participants, DungeonLayout layout, bool isEntry, DateTimeOffset at, CancellationToken ct)
    {
        if (rules.BaseCost(layout, isEntry) is not { } baseCost) return [];
        var payments = new List<DungeonFatiguePayment>(participants.Count);
        bool? shouldConsume = null;
        foreach (var participant in participants)
        {
            var character = participant.CharacterId;
            var snapshot = await benefits.ReadAtAsync(new(participant, layout, isEntry), at, ct, GameplayBenefitScope.Global,
                GameplayBenefitScope.For(participant.AccountId), GameplayBenefitScope.For(character)).ConfigureAwait(false);
            var chargeRule = rules.Quote(baseCost, snapshot.Effects<FatigueCostMultiplier>().ToImmutableArray());
            ushort? remaining = null;
            if (chargeRule.FullCost.HasValue && (shouldConsume ??= await consumption.ShouldConsumeAsync(ct).ConfigureAwait(false)))
                remaining = (await fatigue.BalanceAtAsync(character, at, ct).ConfigureAwait(false)).Remaining;
            var charge = chargeRule.Resolve(character, remaining);
            payments.Add(new(charge, LuckyCharacterBenefitRuleService.BurningWasCharged(snapshot, charge)));
        }
        return payments;
    }
}
