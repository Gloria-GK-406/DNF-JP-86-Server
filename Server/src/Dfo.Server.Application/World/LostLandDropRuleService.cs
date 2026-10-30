using System.Collections.Immutable;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class LostLandDropRuleService(ILostLandDropContentSource source)
    : ILostLandDropRuleService, Dfo.Server.Abstractions.Content.IContentWarmup
{
    // User-approved server policy, not reconstructed original monster rates. Boss identities are
    // anchored by LostLand's boss-chest ACT and three LastTemple MAPs; aliases share one payout.
    private static readonly ImmutableArray<ImmutableHashSet<int>> BossFamilies =
        [ImmutableHashSet.Create(69646), ImmutableHashSet.Create(69584, 69591),
         ImmutableHashSet.Create(69625, 69634), ImmutableHashSet.Create(69660),
         ImmutableHashSet.Create(69619), ImmutableHashSet.Create(69667, 69668, 69669)];
    private readonly Lazy<Task<LostLandDropContent>> content = new(() => source.ReadAsync(CancellationToken.None).AsTask());
    public string Name => "lost-land-drops";

    public async ValueTask<int> WarmAsync(CancellationToken cancellationToken) =>
        (await content.Value.WaitAsync(cancellationToken).ConfigureAwait(false)).Items.Count;

    public async ValueTask<LostLandDropPlan> PrepareAsync(DungeonCellCreationContext context,
        RoomActorDescription described, ushort sequence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = described.Actor;
        if (context.Layout.Mode is not DungeonRunMode.LostLand
            || described.IsAiCharacter || described.IsHellTemplate
            || !LostLandEnemyRules.IsEnemy(actor)) return LostLandDropPlan.Empty;
        var table = await content.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!table.MapCrystalIds.TryGetValue(context.MapId, out var crystal))
            throw new InvalidDataException($"Unbound LostLand drop map {context.MapId}.");
        if (LostLandEnemyRules.IsForestTribe(actor) && crystal != 10092845)
            return LostLandDropPlan.Empty;
        var random = new DropRandom(DropRandom.SeedFor(context.Layout.Seed, context.MapId,
            context.Cell.X, context.Cell.Y, sequence) ^ 0x4C4F53544C4F4F54UL);
        RolledDrop Item(int id, int count) => table.Items[id] with { Count = count };
        int Amount(int low, int high) => low + random.Next(high - low + 1) - 1;
        var regular = ImmutableArray.CreateBuilder<RolledDrop>();
        var bonus = ImmutableArray.CreateBuilder<RolledDrop>();
        var family = -1;
        for (var i = 0; i < BossFamilies.Length; i++)
            if (BossFamilies[i].Contains(actor.Code)) { family = i; break; }
        if (family == 5 && crystal == 0)
        {
            bonus.Add(Item(10092849, Amount(40, 60)));
            bonus.Add(Item(10092856, 3));
            if (random.Hits(20, 100)) bonus.Add(table.Equipment[random.Next(table.Equipment.Length) - 1]);
        }
        else if (family is >= 0 and < 5 && crystal == 10092844 + family)
        {
            regular.Add(Item(crystal, Amount(20, 30)));
            bonus.Add(Item(10092849, Amount(8, 12)));
            if (random.Hits(25, 100)) bonus.Add(Item(10092856, 1));
        }
        else
        {
            if (actor.Code == 59822)
            {
                regular.Add(Item(10092849, Amount(3, 5)));
                if (random.Hits(50, 100)) regular.Add(Item(10092856, 1));
            }
            else if (crystal != 0)
            {
                if (actor.ActorType >= 2 || described.IsNamedMonster)
                {
                    regular.Add(Item(crystal, Amount(6, 8)));
                    if (random.Hits(25, 100)) regular.Add(Item(10092849, Amount(1, 2)));
                }
                else if (actor.ActorType == 1)
                {
                    regular.Add(Item(crystal, Amount(3, 5)));
                    if (random.Hits(10, 100)) regular.Add(Item(10092849, 1));
                }
                else if (random.Hits(45, 100)) regular.Add(Item(crystal, Amount(1, 3)));
            }
            return new(regular.ToImmutable());
        }
        return new(regular.ToImmutable(), new(BossFamilies[family], bonus.ToImmutable()));
    }
}
