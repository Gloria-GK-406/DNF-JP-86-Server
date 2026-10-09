using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterDungeonDifficultyDomainService(ICharacterDungeonPermissionTable table, IDungeonDifficultyRuleService rules)
    : ICharacterDungeonDifficultyDomainService
{
    public async ValueTask<DungeonDifficulty> GetMaximumAsync(
        CharacterId characterId, int dungeonId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dungeonId);
        var recorded = await GetRecordedAsync(characterId, cancellationToken).ConfigureAwait(false);
        return recorded.SingleOrDefault(value => value.DungeonId == dungeonId)?.Maximum
            ?? DungeonDifficulty.Normal;
    }

    public async ValueTask<IReadOnlyList<CharacterDungeonDifficulty>> GetRecordedAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(characterId.Value);
        var rows = await table.ReadAsync(characterId, cancellationToken).ConfigureAwait(false);
        if (rows.Any(row => row.DungeonId <= 0 || row.ClearState is < 0 or > 4))
            throw new InvalidDataException("Stored dungeon difficulty is outside the ordinary A21 range.");
        // Legacy rows are keyed by display order, not dungeon. Read each dungeon once without losing its maximum.
        return rows.GroupBy(row => row.DungeonId)
            .Select(group => new CharacterDungeonDifficulty(group.Key, (DungeonDifficulty)group.Max(row => row.ClearState)))
            .OrderBy(value => value.DungeonId).ToArray();
    }

    public async ValueTask<DungeonDifficulty> UnlockAsync(
        CharacterId characterId, int dungeonId, DungeonDifficulty difficulty, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(difficulty)) throw new ArgumentOutOfRangeException(nameof(difficulty));
        var current = await GetMaximumAsync(characterId, dungeonId, cancellationToken).ConfigureAwait(false);
        if (difficulty > current)
            await table.RaiseMaximumAsync(characterId, dungeonId, (byte)difficulty, cancellationToken).ConfigureAwait(false);
        return await GetMaximumAsync(characterId, dungeonId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<CharacterDungeonDifficulty?> UnlockAfterClearAsync(
        CharacterId characterId, int dungeonId, DungeonDifficulty cleared, CancellationToken cancellationToken)
    {
        var next = await rules.NextAfterClearAsync(dungeonId, cleared, cancellationToken).ConfigureAwait(false);
        if (next is null || next <= await GetMaximumAsync(characterId, dungeonId, cancellationToken).ConfigureAwait(false))
            return null;
        return new(dungeonId, await UnlockAsync(characterId, dungeonId, next.Value, cancellationToken).ConfigureAwait(false));
    }
}
