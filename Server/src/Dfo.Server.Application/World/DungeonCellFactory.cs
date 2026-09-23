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
    IExperienceRuleService experienceRules) : IDungeonCellFactory
{
    public async ValueTask<DungeonCellSeed> CreateAsync(
        DungeonCellCreationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var leader = context.Participants.Single(participant => participant.IsLeader);
        var participants = context.Participants
            .Select(participant => new MonsterDropParticipant(participant.AccountId, participant.CharacterId))
            .ToImmutableArray();
        var contents = await rooms.DescribeAsync(context, cancellationToken)
            .ConfigureAwait(false);

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
            var isMonster = experienceRules.IsMonster(described.Actor.ActorType);
            IReadOnlyList<RolledDrop> pending = [];
            if (isMonster || described.IsAiCharacter)
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
                var generated = new List<RolledDrop>(await loot.GenerateGroundAsync(
                    described.Actor.Code, dropContext, cancellationToken).ConfigureAwait(false));
                foreach (var participant in participants)
                    generated.AddRange(await loot.GeneratePersonalAsync(
                        described.Actor.Code, participant.CharacterId, dropContext, cancellationToken)
                        .ConfigureAwait(false));
                pending = generated;
            }

            actors.Add(new DungeonCellActorState(
                sequence,
                described.Actor,
                described.IsAiCharacter,
                IsDead: false,
                pending.ToArray(),
                isMonster));
        }

        return new DungeonCellSeed(
            context.Cell,
            context.MapId,
            contents.FirstActorSequence,
            actors,
            groundItems);
    }
}
