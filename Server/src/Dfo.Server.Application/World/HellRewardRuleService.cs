using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class HellRewardRuleService(IHellRewardDefinitionSource source,
    IHellDungeonDefinitionSource parties, IDropItemPoolSource pools, HellRewardOptions options) : IHellRewardRuleService
{
    public bool EligibleForGroupReward(int code, bool isAiCharacter) => !source.IsHellMonster(code, isAiCharacter);

    public async ValueTask<IReadOnlyList<RolledDrop>> DrawGroupAsync(HellGroupRewardContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (context.HellDifficulty is < 1 or > 4 || (int)context.Drop.Difficulty is < 0 or > 4
            || context.GroupIndex <= 0 || options.EquipmentRatePercent < 0
            || options.EpicPieceAttempts.Count != 5 || options.EpicPieceAttempts.Any(x => x < 0))
            throw new ArgumentOutOfRangeException(nameof(context));
        var definition = source.Load();
        var band = definition.Bands.SingleOrDefault(x => context.BasisLevel >= x.Low && context.BasisLevel <= x.High);
        if (band is null || !definition.GradeBands.TryGetValue(context.BasisLevel, out var grades)) return [];
        var party = await parties.GetPartyAsync(token).ConfigureAwait(false);
        var attempts = party.ManualDifficulties.Single(x => x.Difficulty == context.HellDifficulty).RewardRollCount;
        if (attempts < 0 || attempts > 255 || context.HellDifficulty > definition.RarityThresholds.Count)
            throw new InvalidDataException("Unsupported hell reward difficulty.");
        var pool = pools.Load(DropItemFamily.Equipment);
        var seed = DropRandom.SeedFor(context.Drop.Seed, context.Drop.MapId,
            context.Drop.Cell.X, context.Drop.Cell.Y, context.GroupIndex) ^ 0x48454C4C52455744UL;
        var random = new DropRandom(seed);
        var drops = new List<RolledDrop>();
        var rate = (int)Math.Min(1000, (long)band.Rates[(int)context.Drop.Difficulty] * options.EquipmentRatePercent / 100);
        void DrawEquipment(DropRandom stream)
        {
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                // 70 uses 0..1000 inclusive; keep zero disabled rather than a spurious 1/1001 hit.
                if (rate <= 0 || stream.Next(1001) - 1 > rate) continue;
                var rarityRoll = stream.Next(1000001) - 1;
                var ladder = definition.RarityThresholds[context.HellDifficulty - 1];
                if (HellRewardRules.RarityFor(rarityRoll, ladder) is not { } rarity) continue;
                if (DropRules.Pick(pool, grades, rarity, stream) is { } item)
                    drops.Add(new(item.ItemId, 1, rarity, DropItemKind.Equipment, item.Durability));
            }
        }
        DrawEquipment(random);
        var epicCandidates = pool.Band(grades, DropRarity.Epic).Where(x => definition.EpicPieces.ContainsKey(x.ItemId)).ToArray();
        var piecePool = new DropItemPool(DropItemFamily.Equipment,
            new Dictionary<(int, int), IReadOnlyList<DropPoolEntry>> { [(0, (int)DropRarity.Epic)] = epicCandidates });
        var pieceAttempts = options.EpicPieceAttempts[(int)context.Drop.Difficulty] + (context.HellDifficulty == 1 ? 1 : 0);
        void DrawPieces(DropRandom stream)
        {
            for (var attempt = 0; attempt < pieceAttempts; attempt++)
                if (DropRules.Pick(piecePool, new(0, 0), DropRarity.Epic, stream) is { } item)
                    drops.Add(new(definition.EpicPieces[item.ItemId], 1, DropRarity.Epic, DropItemKind.Stackable));
        }
        DrawPieces(random);
        for (var round = 1; round < context.DrawMultipliers.For(DropDrawEntry.HellEquipment); round++)
            DrawEquipment(new DropRandom(DropRandom.ForRound(seed, new(DropDrawEntry.HellEquipment, round))));
        for (var round = 1; round < context.DrawMultipliers.For(DropDrawEntry.HellEpicPiece); round++)
            DrawPieces(new DropRandom(DropRandom.ForRound(seed, new(DropDrawEntry.HellEpicPiece, round))));
        return drops.ToArray();
    }
}
