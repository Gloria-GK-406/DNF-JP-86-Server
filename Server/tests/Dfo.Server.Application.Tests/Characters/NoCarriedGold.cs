using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// A character's gold, for the cases that are not about gold.
/// </summary>
/// <remarks>
/// <see cref="PlayingCharacter"/> answers questions about item use as well as about money, and the
/// cases about item use have to construct one. This stands in for the money half so that they can,
/// and it answers as a character this server does not have: nothing is carried, nothing is given,
/// nothing can be taken. A case that started to depend on gold would fail against it rather than
/// quietly pass against a made-up figure.
/// </remarks>
internal sealed class NoCarriedGold : ICharacterCarriedGoldTable
{
    public ValueTask<CarriedGoldRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<CarriedGoldRow?>(null);

    public ValueTask<int> AddAsync(
        CharacterId characterId, int amount, int ceiling, CancellationToken cancellationToken) =>
        ValueTask.FromResult(0);

    public ValueTask<int?> TryTakeAsync(
        CharacterId characterId, int amount, CancellationToken cancellationToken) =>
        ValueTask.FromResult<int?>(null);
}

/// <summary>A carry cap for the cases that are not about the carry cap.</summary>
/// <remarks>
/// It answers with the stored upgrade and reads no archive, which is enough for a construction
/// that never asks. A case that started to depend on the archive's floor would see this figure
/// rather than the real one and would have to say so.
/// </remarks>
internal sealed class StoredCarryLimitOnly : ICarriedGoldRuleService
{
    public int CarryLimitFor(int characterLevel, int storedCarryLimit) =>
        Math.Max(0, storedCarryLimit);
}
