using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.World;

internal sealed class LuckySharedDropBenefits(LuckyCharacterBenefitRuleService rules)
    : IGameplayBenefitSource<DungeonCellCreationContext>
{
    public ValueTask<GameplayBenefitBatch> DescribeAsync(
        DungeonCellCreationContext context, DateTimeOffset at, CancellationToken ct) =>
        ValueTask.FromResult(new GameplayBenefitBatch("lucky-character", DropBenefitScopes.Run(context.RunId),
            rules.SharedDrops(context.Participants)));
}

internal sealed class GrowthBossDropBenefits(IPremiumServiceDomainService premiums)
    : IGameplayBenefitSource<MonsterDropContext>
{
    public async ValueTask<GameplayBenefitBatch> DescribeAsync(
        MonsterDropContext context, DateTimeOffset at, CancellationToken ct)
    {
        var percent = await premiums.GetBossEquipmentDropBonusPercentAsync(
            context.Participants.Select(p => p.AccountId).ToArray(), ct).ConfigureAwait(false);
        return new("premium-services", DropBenefitScopes.Run(context.RunId),
            percent > 0 ? [new("growth-boss-equipment", "growth contract", 0, new BossEquipmentDropBenefit(percent))] : []);
    }
}

internal sealed class QuestHelperDropBenefits(IPremiumServiceDomainService premiums)
    : IGameplayBenefitSource<AccountId>
{
    public async ValueTask<GameplayBenefitBatch> DescribeAsync(AccountId account, DateTimeOffset at, CancellationToken ct)
    {
        var active = await premiums.For(account).HasBenefitAsync(PremiumBenefit.QuestItemDropBonus, ct).ConfigureAwait(false);
        return new("quest-helper", GameplayBenefitScope.For(account),
            active ? [new("quest-materials", "quest helper", 0, new QuestHelperDropBenefit())] : []);
    }
}

internal static class DropBenefitScopes
{
    internal static GameplayBenefitScope Run(DungeonRunId run) => new("dungeon-run", run.Value.ToString("D"));
}
