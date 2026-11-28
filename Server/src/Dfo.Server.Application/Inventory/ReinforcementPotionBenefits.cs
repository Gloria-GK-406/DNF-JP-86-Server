using System.Collections.Immutable;
using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;

namespace Dfo.Server.Application.Inventory;

internal sealed class ReinforcementPotionBenefits(ICharacterItemEffectTable rows, IItemUseRuleDefinitionSource definitions)
    : IGameplayBenefitSource<SelectedSessionIdentity>
{
    public async ValueTask<GameplayBenefitBatch> DescribeAsync(
        SelectedSessionIdentity session, DateTimeOffset at, CancellationToken ct)
    {
        var now = at.ToUnixTimeSeconds();
        var active = (await rows.ReadByCharacterAsync(session.CharacterId, ct).ConfigureAwait(false))
            .Where(item => item.Value > now)
            .GroupBy(item => item.ItemId);
        var found = ImmutableArray.CreateBuilder<GameplayBenefit>();
        foreach (var item in active)
        {
            if (await definitions.GetAsync(item.Key, ct).ConfigureAwait(false) is not
                { IsReadable: true, ReinforcementPotion: { } potion })
                continue;
            found.Add(new($"item-{item.Key}", potion.Name, 0,
                new EquipmentReinforcementChanceBenefit(potion.SuccessUnits,
                    potion.IsCrystal ? "reinforcement-crystal" : "reinforcement-secret-potion"),
                EndsAt: DateTimeOffset.FromUnixTimeSeconds(item.Max(row => row.Value))));
        }
        return new("reinforcement-potions", GameplayBenefitScope.For(session.CharacterId), found.ToImmutable());
    }
}
