using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>Materializes one complete cell before a dungeon run publishes it.</summary>
internal sealed class DungeonCellFactory(
    IDungeonRoomRuleService rooms,
    IItemPlacementRuleService placementRules,
    IMonsterDropDomainService loot,
    IExperienceRuleService experienceRules,
    IQuestEnemyDropDomainService questLoot,
    IQuestHelperGroundBonusDomainService questBonus,
    IHellRewardDomainService hellRewards, ILostLandDropRuleService lostLandDrops) : IDungeonCellFactory
{
    public async ValueTask<DungeonCellSeed> CreateAsync(
        DungeonCellCreationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Layout.Mode is DungeonRunMode.Altar)
            return DungeonCellSeed.CompleteEmpty(context.Cell, context.MapId) with { MechanismBlocksClear = true };
        var leader = context.Participants.Single(participant => participant.IsLeader);
        var participants = context.Participants
            .Select(participant => new MonsterDropParticipant(participant.AccountId, participant.CharacterId))
            .ToImmutableArray();
        var contents = await rooms.DescribeAsync(context, cancellationToken)
            .ConfigureAwait(false);
        if (context.Layout.Mode is DungeonRunMode.Guardian { Plan: var guardian })
            // Native ProcStage starts at zero and shares this counter with later 035D summons.
            // A21 reads 035D identifiers as signed words; ordinary room random bases are unsuitable.
            return new(context.Cell, context.MapId, 0,
                contents.Actors.Where(actor => guardian.Towers.ContainsKey(actor.Actor.Code)).Select((actor, index) => new DungeonCellActorState(
                    checked((ushort)index), actor.Actor with
                    { Code = guardian.Towers[actor.Actor.Code].ResourceId, Level = checked((byte)guardian.Towers[actor.Actor.Code].Level), IsHostile = false }, actor.IsAiCharacter,
                    false, [], IsMonster: false)).ToArray(), []) { MechanismBlocksClear = true };

        var groundItems = new List<DungeonGroundItem>(contents.GroundDrops.Count);
        foreach (var ground in contents.GroundDrops)
        {
            groundItems.Add(new DungeonGroundItem(
                ground.Number,
                ground.Drop.ItemId,
                Math.Max(1, ground.Drop.Count),
                ground.Drop.Durability,
                await placementRules.IsEquipmentAsync(ground.Drop.ItemId, cancellationToken)
                    .ConfigureAwait(false),
                DungeonGroundItemSource.Declared(ground.Drop.ObjectIndex)));
        }

        var actors = new List<DungeonCellActorState>(contents.Actors.Count);
        for (var index = 0; index < contents.Actors.Count; index++)
        {
            var described = contents.Actors[index];
            var sequence = checked((ushort)(contents.FirstActorSequence + index));
            // Hell actors do not participate in ordinary boss XP/automatic room suppression.
            var isDummy = described.Actor.Declaration?.IsDummy == true && !described.RequiredCombatant;
            var isMonster = !isDummy && !described.IsHellTemplate
                && experienceRules.IsMonster(described.Actor.ActorType);
            IReadOnlyList<RolledDrop> pending = [];
            LostLandKillBonus? lostLandBonus = null;
            var eligibleForGroupReward = false;
            if (!isDummy && (described.IsHellTemplate || isMonster || described.IsAiCharacter))
            {
                var dropContext = new MonsterDropContext(
                    context.RunId,
                    context.Layout.DungeonId,
                    context.Layout.Seed,
                    context.Cell,
                    context.MapId,
                    context.Difficulty,
                    sequence,
                    context.Layout.LevelOf(described.Actor.Level),
                    described.Actor.ActorType,
                    leader.CharacterId,
                    participants)
                {
                    RandomDropCount = described.Actor.RandomDropCount,
                    SourceType = described.IsAiCharacter
                        ? IndependentDropSourceType.APC : IndependentDropSourceType.Monster,
                };
                if (described.IsHellTemplate)
                {
                    if (described.HellGroupIndex is not > 0) throw new InvalidDataException("Hell actor is missing its group identity.");
                    var frozen = await hellRewards.FreezeActorAsync(described.Actor.Code, dropContext, cancellationToken).ConfigureAwait(false);
                    pending = frozen.Loot;
                    eligibleForGroupReward = frozen.EligibleForGroupReward;
                }
                else
                {
                    var generated = new List<RolledDrop>(await loot.GenerateGroundAsync(
                        described.Actor.Code, dropContext, cancellationToken).ConfigureAwait(false));
                    foreach (var participant in participants)
                        generated.AddRange(await loot.GeneratePersonalAsync(
                            described.Actor.Code, participant.CharacterId, dropContext, cancellationToken)
                            .ConfigureAwait(false));
                    var lostLand = await lostLandDrops.PrepareAsync(context, described, sequence, cancellationToken)
                        .ConfigureAwait(false);
                    generated.AddRange(lostLand.Regular);
                    lostLandBonus = lostLand.Bonus;
                    pending = generated;
                }
            }

            actors.Add(new DungeonCellActorState(
                sequence,
                described.Actor,
                described.IsAiCharacter,
                IsDead: false,
                pending.ToArray(),
                isMonster) { IsNamedMonster = isMonster && described.IsNamedMonster, LostLandBonus = lostLandBonus, RequiredCombatant = described.RequiredCombatant,
                    IsHellTemplate = described.IsHellTemplate, HellOrder = described.HellOrder,
                    HellGroupIndex = described.HellGroupIndex, EligibleForHellGroupReward = eligibleForGroupReward,
                    ObjectTemplateOwner = described.ObjectTemplateOwner, ObjectTemplateIndex = described.ObjectTemplateIndex });
        }

        var hellGroups = new List<DungeonCellHellGroupState>();
        foreach (var group in actors.Where(x => x.IsHellTemplate).GroupBy(x => x.HellGroupIndex!.Value))
        {
            var hell = (context.Layout.Mode as DungeonRunMode.Hell)?.Plan ?? throw new InvalidDataException("Hell actors require a frozen entry plan.");
            var drop = new MonsterDropContext(context.RunId, context.Layout.DungeonId, context.Layout.Seed,
                context.Cell, context.MapId, context.Difficulty, group.First().Sequence,
                context.Layout.BasisLevel, 0, leader.CharacterId, participants);
            var pending = group.Any(x => x.EligibleForHellGroupReward)
                ? await hellRewards.FreezeGroupAsync(new(drop, hell.Difficulty, group.Key, context.Layout.BasisLevel), cancellationToken).ConfigureAwait(false)
                : [];
            hellGroups.Add(new(group.Key, group.Select(x => x.Sequence).ToArray(), pending.ToArray()));
        }

        var objects = new List<DungeonCellObjectState>(contents.Objects.Count);
        foreach (var declared in contents.Objects)
        {
            var request = new QuestEnemyDropRequest(QuestEnemyType.PassiveObject, declared.Code,
                declared.Id.RandomIdentity, context.Layout.DungeonId, context.Difficulty,
                context.Layout.Seed, context.Cell.X, context.Cell.Y, context.MapId);
            var pending = new List<RolledDrop>();
            foreach (var participant in participants)
                pending.AddRange(await questLoot.DrawAsync(request, participant, cancellationToken).ConfigureAwait(false));
            objects.Add(new(declared, pending.ToArray()));
        }

        var seed = new DungeonCellSeed(
            context.Cell,
            context.MapId,
            contents.FirstActorSequence,
            actors,
            groundItems) { Objects = objects.ToArray(), HellGroups = hellGroups.ToArray(), Mechanisms = contents.Mechanisms };
        return await questBonus.ApplyAsync(context, seed, cancellationToken).ConfigureAwait(false);
    }
}
