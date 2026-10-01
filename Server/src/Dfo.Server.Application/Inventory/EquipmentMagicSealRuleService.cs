using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentMagicSealRuleService(IEquipmentMagicSealContentSource content)
    : IEquipmentMagicSealRuleService
{
    public async ValueTask<EquipmentMagicSealRuleDecision> DecideAsync(int equipmentItemId,
        IReadOnlyList<RandomOptionState> existingOptions, byte? changedIndex,
        int seed, CancellationToken ct)
    {
        if (existingOptions.Count != 3)
            return new(EquipmentMagicSealRejection.InvalidRequest);
        var definition = await content.GetEquipmentAsync(equipmentItemId, ct)
            .ConfigureAwait(false);
        if (definition is null || definition.ItemId != equipmentItemId
            || definition.UnsealGoldCost < 0 || definition.ChangeGoldCost < 0)
            return new(EquipmentMagicSealRejection.UnsupportedContent);

        var count = existingOptions.Count(option => option.Type != 0);
        var random = new Random(seed);
        if (changedIndex is null)
        {
            if (count != 0) return new(EquipmentMagicSealRejection.AlreadyUnsealed);
            var rolled = RollInitial(definition, random);
            return rolled.Count == 0
                ? new(EquipmentMagicSealRejection.UnsupportedContent)
                : new(EquipmentMagicSealRejection.None, rolled,
                    definition.UnsealGoldCost, seed);
        }
        if (count == 0) return new(EquipmentMagicSealRejection.NotUnsealed);
        if (existingOptions.Take(count).Any(option => option.Type == 0)
            || existingOptions.Skip(count).Any(option => option.Type != 0)
            || changedIndex.Value >= count)
            return new(EquipmentMagicSealRejection.InvalidOption);
        var next = existingOptions.ToArray();
        var used = next.Where((_, index) => index != changedIndex.Value)
            .Select(option => option.Type).ToHashSet();
        var replacement = RollCandidate(definition.ChangeGroups,
            changedIndex.Value, used, definition.ChangeValues, random);
        if (replacement is null)
            return new(EquipmentMagicSealRejection.UnsupportedContent);
        next[changedIndex.Value] = replacement;
        return new(EquipmentMagicSealRejection.None, Array.AsReadOnly(next),
            definition.ChangeGoldCost, seed);
    }

    private static IReadOnlyList<RandomOptionState> RollInitial(
        EquipmentMagicSealDefinition definition, Random random)
    {
        var quantity = SelectWeighted(definition.Quantities,
            value => value.Weight, random)?.Count ?? 0;
        if (quantity is < 1 or > 3 || definition.InitialGroups.Count == 0) return [];
        var selected = new List<RandomOptionState>();
        var used = new HashSet<byte>();
        for (var index = 0; index < quantity; index++)
        {
            var option = RollCandidate(definition.InitialGroups, index, used, definition.InitialValues, random);
            if (option is null) return [];
            selected.Add(option);
            used.Add(option.Type);
        }
        return selected.AsReadOnly();
    }

    private static RandomOptionState? RollCandidate(
        IReadOnlyList<IReadOnlyList<MagicSealCandidate>> groups, int index,
        HashSet<byte> used, IReadOnlyList<MagicSealValueBand> values, Random random)
    {
        if (groups.Count == 0) return null;
        var candidates = groups[Math.Min(index, groups.Count - 1)]
            .Where(candidate => candidate.Type != 0
                && !used.Contains(candidate.Type)).ToArray();
        var candidate = SelectWeighted(candidates, candidate => candidate.Weight, random);
        var band = SelectWeighted(values, value => value.Weight, random);
        if (candidate is null || band is null) return null;
        if (band.Minimum < 0 || band.Maximum > byte.MaxValue || band.Minimum > band.Maximum)
            throw new InvalidDataException("Invalid magic-seal quality range.");
        // Both quality bytes are independently sampled. Stat magnitude is decoded by the client
        // from the option template; sending its endpoints here turns nearly every item into S.
        return new(candidate.Type, checked((byte)random.Next(band.Minimum, band.Maximum + 1)),
            checked((byte)random.Next(band.Minimum, band.Maximum + 1)));
    }

    private static T? SelectWeighted<T>(IReadOnlyList<T> values, Func<T, int> weight,
        Random random) where T : class
    {
        var total = values.Sum(value => Math.Max(0, weight(value)));
        if (total <= 0) return null;
        var roll = random.Next(total);
        foreach (var value in values)
        {
            roll -= Math.Max(0, weight(value));
            if (roll < 0) return value;
        }
        throw new InvalidOperationException("Invalid magic-seal weights.");
    }
}
