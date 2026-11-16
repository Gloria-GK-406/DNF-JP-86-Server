namespace Dfo.Server.Abstractions.World;

/// <summary>Delivery capabilities supplied by the protocol composition boundary.</summary>
public sealed record DropMaterializationLimits(
    int InitialGroundItems, int DeathGroundItems, int IndependentDrops, int LastGroundItemNumber);

public readonly record struct DropMaterializationCount(int Generated, int Kept)
{
    public int Discarded => Generated - Kept;
}

public sealed record DropMaterializationSelection<T>(IReadOnlyList<T> Items, DropMaterializationCount Count);

/// <summary>Ordered admission only: draws, recipient eligibility and state changes belong to callers.</summary>
public abstract record DropMaterializationRule
{
    public abstract DropMaterializationSelection<GroundDrop> SelectInitial(IReadOnlyList<GroundDrop> drops);
    public abstract DropMaterializationSelection<RolledDrop> SelectIndependent(IReadOnlyList<RolledDrop> drops);
    public abstract DropMaterializationSelection<RolledDrop> SelectDeath(IEnumerable<RolledDrop> eligibleGroundDrops);
    public abstract DropMaterializationCount InspectFrozenDeath(int groundDropCount);
    public abstract int AdmitGroundItemNumbers(int nextNumber, int requested);
}

public interface IDropMaterializationRuleService
{
    DropMaterializationRule Rule { get; }
}
