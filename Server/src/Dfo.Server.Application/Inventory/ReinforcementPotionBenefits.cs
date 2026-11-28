using System.Collections.Immutable;
using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;

namespace Dfo.Server.Application.Inventory;

internal sealed class ReinforcementPotionBenefits(ITimedItemEffectTable rows, IItemUseRuleService rules)
    : IGameplayBenefitSource<SelectedSessionIdentity>
{
    public async ValueTask<GameplayBenefitBatch> DescribeAsync(
        SelectedSessionIdentity session, DateTimeOffset at, CancellationToken ct)
    {
        var active = await rows.ReadActiveAsync(session.CharacterId, at, ct).ConfigureAwait(false);
        var found = ImmutableArray.CreateBuilder<GameplayBenefit>();
        foreach (var item in active)
        {
            if (await rules.RuleForAsync(item.ItemId, ct).ConfigureAwait(false) is not
                { IsReadable: true, ReinforcementPotion: { } potion })
                continue;
            found.Add(new($"item-{item.ItemId}", potion.Name, 0,
                EquipmentUpgradeRuleService.PotionChanceBenefit(potion), EndsAt: item.ExpiresAt));
        }
        return new("reinforcement-potions", GameplayBenefitScope.For(session.CharacterId), found.ToImmutable());
    }
}
