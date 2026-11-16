using System.Collections.Immutable;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class DropMaterializationRuleService : IDropMaterializationRuleService
{
    public DropMaterializationRuleService(DropMaterializationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.InitialGroundItems);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.DeathGroundItems);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.IndependentDrops);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.LastGroundItemNumber);
        Rule = new MaterializationRule(limits);
    }

    public DropMaterializationRule Rule { get; }

    private sealed record MaterializationRule(DropMaterializationLimits Limits) : DropMaterializationRule
    {
        public override DropMaterializationSelection<GroundDrop> SelectInitial(IReadOnlyList<GroundDrop> drops) =>
            Select(drops, Limits.InitialGroundItems);

        public override DropMaterializationSelection<RolledDrop> SelectIndependent(IReadOnlyList<RolledDrop> drops) =>
            Select(drops, Limits.IndependentDrops);

        public override DropMaterializationSelection<RolledDrop> SelectDeath(IEnumerable<RolledDrop> eligibleGroundDrops) =>
            Select(eligibleGroundDrops.ToArray(), Limits.DeathGroundItems);

        public override DropMaterializationCount InspectFrozenDeath(int groundDropCount) =>
            Count(groundDropCount, Limits.DeathGroundItems);

        public override int AdmitGroundItemNumbers(int nextNumber, int requested)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nextNumber);
            ArgumentOutOfRangeException.ThrowIfNegative(requested);
            return Math.Min(requested, Math.Max(0, Limits.LastGroundItemNumber - nextNumber + 1));
        }

        private static DropMaterializationSelection<T> Select<T>(IReadOnlyList<T> drops, int maximum)
        {
            var count = Count(drops.Count, maximum);
            return new(drops.Take(count.Kept).ToImmutableArray(), count);
        }

        private static DropMaterializationCount Count(int generated, int maximum)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(generated);
            return new(generated, Math.Min(generated, maximum));
        }
    }
}
