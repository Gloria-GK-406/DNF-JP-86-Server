using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

internal sealed class HellRewardDomainService(IHellRewardRuleService rules,
    IIndependentDropRuleService independent, IMonsterDropDomainService personal,
    ILogger<HellRewardDomainService> logger) : IHellRewardDomainService
{
    public async ValueTask<HellActorReward> FreezeActorAsync(int code, MonsterDropContext context, CancellationToken token)
    {
        var eligible = rules.EligibleForGroupReward(code, context.SourceType == Dfo.Server.Abstractions.Content.IndependentDropSourceType.APC);
        var loot = new List<RolledDrop>(await independent.DrawAsync(new(code, context), token).ConfigureAwait(false));
        foreach (var participant in context.Participants)
            loot.AddRange(await personal.GeneratePersonalAsync(code, participant.CharacterId, context, token).ConfigureAwait(false));
        return new(loot.ToArray(), eligible);
    }

    public async ValueTask<IReadOnlyList<RolledDrop>> FreezeGroupAsync(HellGroupRewardContext context, CancellationToken token)
    {
        var loot = await rules.DrawGroupAsync(context, token).ConfigureAwait(false);
        logger.LogInformation("Hell group reward frozen: Run={RunId} Dungeon={DungeonId} Cell={Cell} Group={GroupIndex} HellDifficulty={HellDifficulty} Equipment={EquipmentCount} Pieces={PieceCount}",
            context.Drop.RunId, context.Drop.DungeonId, context.Drop.Cell, context.GroupIndex, context.HellDifficulty,
            loot.Count(x => x.Kind == DropItemKind.Equipment), loot.Count(x => x.Kind == DropItemKind.Stackable));
        return loot;
    }
}
