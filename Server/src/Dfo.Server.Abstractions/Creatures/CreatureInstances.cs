using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Creatures;

/// <summary>Finds identities, independently of their current inventory positions.</summary>
public interface ICreatureCollection
{
    ValueTask<IPetInstance?> FindPetAsync(int itemUid, CancellationToken cancellationToken);
    ValueTask<IEggInstance?> FindEggAsync(int itemUid, CancellationToken cancellationToken);
    ValueTask<IPetInstance?> CurrentPetAsync(CancellationToken cancellationToken);
}

/// <summary>A pet identity in the current use case. Obtain a new instance for a new transaction.</summary>
public interface IPetInstance
{
    CharacterId Owner { get; }
    int ItemUid { get; }
    ValueTask<CreatureState> ReadAsync(CancellationToken cancellationToken);
    ValueTask<CreatureRuntimeChange> AddStomachAsync(int amount, CancellationToken cancellationToken);
    ValueTask<CreatureRuntimeChange?> AddExpAsync(int amount, CancellationToken cancellationToken);
    ValueTask<CreatureState?> RenameAsync(byte[] nameBytes, CancellationToken cancellationToken);
    ValueTask<CreatureTransformationReceipt?> EvolveAsync(CancellationToken cancellationToken);
}

/// <summary>An egg identity. Birth preserves its UID and returns the new pet instance.</summary>
public interface IEggInstance
{
    CharacterId Owner { get; }
    int ItemUid { get; }
    ValueTask<IPetInstance?> BirthAsync(CancellationToken cancellationToken);
}

/// <summary>Constructs transaction-bound identities; implementations also coordinate the online clock.</summary>
public interface ICreatureInstanceDomainService
{
    ICreatureCollection Of(CharacterId characterId);
}
