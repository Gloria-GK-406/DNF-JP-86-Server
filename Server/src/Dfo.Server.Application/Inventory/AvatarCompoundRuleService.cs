using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Application.Characters;

namespace Dfo.Server.Application.Inventory;

internal sealed class AvatarCompoundRuleService(IAvatarCompoundDefinitionSource content) : IAvatarCompoundRuleService
{
    public async ValueTask<AvatarCompoundPlan> PlanAsync(byte job, byte level, AvatarCompoundInput input, int materialId,
        ItemSnapshot first, ItemSnapshot second, int seed, CancellationToken ct)
    {
        if (input.Category > 1 || input.RandomTarget > 1 || (input.TargetItemId == 1) != (input.RandomTarget == 1)
            || input.TargetItemId <= 0 || input.Ability > byte.MaxValue)
            return new(AvatarCompoundRejection.InvalidRequest);
        var definition = await content.ForJobAsync(job, ct).ConfigureAwait(false);
        if (definition is null) return new(AvatarCompoundRejection.UnsupportedContent);
        var material = definition.Materials.SingleOrDefault(x => x.Category == input.Category && x.ItemId == materialId);
        if (material is null) return new(AvatarCompoundRejection.MissingMaterial);
        var a = await content.ItemAsync(first.ItemId, ct).ConfigureAwait(false);
        var b = await content.ItemAsync(second.ItemId, ct).ConfigureAwait(false);
        if (a is null || b is null || a.EquipmentType != b.EquipmentType
            || !Grade(a.Grade) || !Grade(b.Grade) || !Usable(a) || !Usable(b)) return new(AvatarCompoundRejection.InvalidAvatar);
        if (!a.Permanent || !b.Permanent) return new(AvatarCompoundRejection.UnsupportedLifetime);
        var part = definition.Parts.SingleOrDefault(x => x.EquipmentType == a.EquipmentType);
        if (part is null) return new(AvatarCompoundRejection.UnsupportedContent);
        var random = new Random(seed);
        var targetId = input.RandomTarget == 1 ? Pick(part.Advanced, random) : input.TargetItemId;
        if (!part.Advanced.Any(x => x.ItemId == targetId)) return new(AvatarCompoundRejection.InvalidChoice);
        var target = await content.ItemAsync(targetId, ct).ConfigureAwait(false);
        if (target is null || target.EquipmentType != part.EquipmentType || target.Grade != definition.AdvancedGrade
            || !target.Permanent || !Ability(target, input.Ability) || !Usable(target)) return new(AvatarCompoundRejection.InvalidChoice);
        // A21 compatibility policy: additive three tiers; 70's single-result formula is a different version.
        var advancedCount = (a.Grade == definition.AdvancedGrade ? 1 : 0) + (b.Grade == definition.AdvancedGrade ? 1 : 0);
        var rate = (int)Math.Clamp((long)part.RareRate + advancedCount * (long)part.UpperRate / 2
            + (input.Category == 1 ? definition.GoldenBonus : 0), 0, 10000);
        var outputs = ImmutableArray.CreateBuilder<ItemSnapshot>();
        outputs.Add(Create(target, checked((byte)input.Ability)));
        if (random.Next(10000) < rate && !part.Rare.IsEmpty)
        {
            var rare = await content.ItemAsync(Pick(part.Rare, random), ct).ConfigureAwait(false);
            if (rare is null || rare.EquipmentType != part.EquipmentType || !rare.Permanent || !Usable(rare))
                return new(AvatarCompoundRejection.UnsupportedContent);
            var ability = Ability(rare, input.Ability) ? input.Ability
                : rare.Abilities.IsEmpty ? 0 : rare.Abilities[random.Next(rare.Abilities.Length)];
            if (ability is < 0 or > byte.MaxValue) return new(AvatarCompoundRejection.UnsupportedContent);
            outputs.Add(Create(rare, (byte)ability));
        }
        return new(AvatarCompoundRejection.None, outputs.ToImmutable(), material.Count, rate);
        bool Grade(int grade) => grade == definition.NormalGrade || grade == definition.AdvancedGrade;
        bool Usable(AvatarCompoundItemDefinition item) => level >= item.MinimumLevel
            && item.Jobs.IsValid && item.Jobs.Allows(CharacterJobFamilies.Resolve(job));
    }
    public bool SupportsPlusConverter(int itemId) => itemId is 10007246 or 10007247;

    public async ValueTask<AvatarCompoundPlan> PlanPlusAsync(byte job, byte level, int converterId,
        AvatarCompoundPlusInput input, CancellationToken ct)
    {
        if (!SupportsPlusConverter(converterId))
            return new(AvatarCompoundRejection.MissingMaterial);

        var jobFamily = CharacterJobFamilies.Resolve(job);
        var target = await content.ItemAsync(input.TargetItemId, ct).ConfigureAwait(false);
        if (target is not { Grade: 3, Permanent: true } || !Usable(target)
            || input.Ability > byte.MaxValue || !Ability(target, input.Ability))
            return new(AvatarCompoundRejection.InvalidChoice);

        var definition = await content.PlusForJobAsync(converterId, jobFamily, ct).ConfigureAwait(false);
        if (definition is null || !definition.Targets.TryGetValue(target.EquipmentType, out var itemId) || target.ItemId != itemId)
            return new(AvatarCompoundRejection.InvalidChoice);

        foreach (var ingredient in input.Ingredients)
        {
            var item = await content.ItemAsync(ingredient.ItemId, ct).ConfigureAwait(false);
            if (item is null || item.Grade != definition.IngredientGrade || item.EquipmentType != target.EquipmentType || !Usable(item))
                return new(AvatarCompoundRejection.InvalidAvatar);
            if (!item.Permanent)
                return new(AvatarCompoundRejection.UnsupportedLifetime);
        }
        return new(AvatarCompoundRejection.None, [Create(target, (byte)input.Ability)], MaterialCount: 1);

        bool Usable(AvatarCompoundItemDefinition item) => level >= item.MinimumLevel
            && item.Jobs.IsValid && item.Jobs.Allows(jobFamily);
    }

    private static bool Ability(AvatarCompoundItemDefinition item, int ability) =>
        item.Abilities.IsEmpty ? ability == 0 : item.Abilities.Contains(ability);
    private static int Pick(ImmutableArray<AvatarCompoundWeightedItem> pool, Random random)
    {
        if (pool.IsDefaultOrEmpty || pool.Any(x => x.Weight <= 0)) throw new InvalidDataException("Empty or invalid avatar pool.");
        var roll = random.NextInt64(pool.Sum(x => (long)x.Weight));
        foreach (var row in pool) { roll -= row.Weight; if (roll < 0) return row.ItemId; }
        throw new InvalidDataException("Avatar weights did not select a result.");
    }
    private static ItemSnapshot Create(AvatarCompoundItemDefinition item, byte ability) => new()
    {
        ItemId = item.ItemId, Count = 1, LegacyKind = 8, Type = ItemTraits.Avatar, IsPile = false,
        Attribute = ability, SealFlag = item.Sealed ? (byte)1 : (byte)0, AvatarDetail = ArrivingAvatar.Facts(),
    };
}
